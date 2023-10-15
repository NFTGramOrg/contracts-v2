using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using System;
using System.ComponentModel;
using System.Numerics;

namespace NFT
{
    [DisplayName("Gabriel.NFTContract")]
    [ManifestExtra("Author", "Gabriel Antony Xaviour")]
    [ManifestExtra("Email", "gabrielantony56@gmail.com")]
    [ManifestExtra("Description", "This is a Pokemon NFT contract")]
    [SupportedStandards("NEP-11")]
    [ContractPermission("*", "onNEP11Payment")]

    public class NFTContract : SmartContract
    {
        private const byte Prefix_TotalSupply = 0x00;
        private const byte Prefix_Balance = 0x01;
        private const byte Prefix_Account = 0x02;
        private const byte Prefix_TokenState = 0x03;
        private const byte Prefix_Owner = 0xfe;

        [InitialValue("NL2UNxotZZ3zmTYN8bSuhKDHnceYRnj6NR", Neo.SmartContract.ContractParameterType.Hash160)]
        private static readonly UInt160 InitialOwner = default;

        public delegate void OnSetOwnerDelegate(UInt160 account);

        public delegate void OnTransferDelegate(UInt160 from, UInt160 to, BigInteger amount, ByteString tokenId);

        [DisplayName("SetOwner")]
        public static event OnSetOwnerDelegate OnSetNewOwner;

        [DisplayName("Transfer")]
        public static event OnTransferDelegate OnTransfer;

        [Safe]
        public static string Symbol() => "POKIS";

        [Safe]
        public static byte Decimals() => 0;


        private static ByteString GetKey(ByteString tokenId) =>
            CryptoLib.Ripemd160(tokenId);

        [Safe]
        public static BigInteger TotalSupply() =>
            (BigInteger)Storage.Get(new[] { Prefix_TotalSupply });

        [Safe]
        public static BigInteger BalanceOf(UInt160 owner)
        {
            if (owner == null || !owner.IsValid)
                throw new Exception("The argument \"owner\" is invalid.");
            StorageMap balanceMap = new(Storage.CurrentReadOnlyContext, Prefix_Balance);
            return (BigInteger)balanceMap.Get(owner);
        }

        [Safe]
        public static UInt160 OwnerOf(ByteString tokenId)
        {
            if (tokenId.Length > 64)
                throw new Exception("The argument \"tokenId\" is invalid.");
            StorageMap tokenStateMap = new(Storage.CurrentReadOnlyContext, Prefix_TokenState);
            var token = (NFTTokenState)StdLib.Deserialize(tokenStateMap[GetKey(tokenId)]);
            return token.Owner;
        }

        [Safe]
        public static Map<string, object> Properties(ByteString tokenId)
        {
            if (tokenId.Length > 64)
                throw new Exception("The argument \"tokenId\" is invalid.");
            StorageMap tokenStateMap = new(Storage.CurrentReadOnlyContext, Prefix_TokenState);
            var token = (NFTTokenState)StdLib.Deserialize(tokenStateMap[GetKey(tokenId)]);
            return new()
            {
                ["name"] = token.Name,
                ["description"] = token.Description,
                ["tokenURI"] = "https://www.nftgram.in/nft/" + Runtime.ExecutingScriptHash + "?id=" + tokenId,
                ["image"] = token.Image,
            };
        }

        [Safe]
        public static Iterator Tokens()
        {
            StorageMap tokenStateMap = new(Storage.CurrentReadOnlyContext, Prefix_TokenState);
            return tokenStateMap.Find(FindOptions.ValuesOnly | FindOptions.DeserializeValues | FindOptions.PickField1);
        }

        [Safe]
        public static Iterator TokensOf(UInt160 owner)
        {
            if (owner == null || !owner.IsValid)
                throw new Exception("The argument \"owner\" is invalid");
            StorageMap accountMap = new(Storage.CurrentReadOnlyContext, Prefix_Account);
            return accountMap.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);
        }

        public static bool Transfer(UInt160 to, ByteString tokenId, object data)
        {
            if (to == null || !to.IsValid)
                throw new Exception("The argument \"to\" is invalid.");

            StorageMap tokenStateMap = new(Storage.CurrentContext, Prefix_TokenState);

            var key = GetKey(tokenId);
            var token = (NFTTokenState)StdLib.Deserialize(tokenStateMap[key]);

            if (token.IsOwner() == false)
                return false;

            var from = token.Owner;

            if (from != to)
            {
                token.Owner = to;
                tokenStateMap[key] = StdLib.Serialize(token);
                UpdateBalance(from, tokenId, -1);
                UpdateBalance(to, tokenId, +1);
            }

            PostTransfer(from, to, tokenId, data);
            return true;
        }

        public static void Burn(ByteString tokenId)
        {
            if (IsOwner() == false)
                throw new InvalidOperationException("No Authorization!");
            StorageMap tokenStateMap = new(Storage.CurrentContext, Prefix_TokenState);

            var key = GetKey(tokenId);
            var token = (NFTTokenState)StdLib.Deserialize(tokenStateMap[key]);

            tokenStateMap.Delete(tokenId);
            UpdateBalance(token.Owner, tokenId, -1);
            UpdateTotalSupply(-1);
            PostTransfer(token.Owner, null, tokenId, null);
        }

        public static void Create(BigInteger tokenId,string name,UInt160 owner, string description,string image)
        {
            if (!IsOwner())
            {
                throw new Exception("Only the owner can mint");
            }
            Mint(tokenId,new NFTTokenState()
            {
                Owner = owner,
                Name = name,
                Description = description,
                Image = image
            });
        }

        private static void Mint(BigInteger tokenId, NFTTokenState token)
        {
            StorageMap tokenStateMap = new(Storage.CurrentContext, Prefix_TokenState);

            var key = GetKey((ByteString)tokenId);
            tokenStateMap[key] = StdLib.Serialize(token);
            UpdateBalance(token.Owner, (ByteString)tokenId, 1);
            UpdateTotalSupply(1);
            PostTransfer(null, token.Owner, (ByteString)tokenId, null);
        }

        private static void PostTransfer(UInt160 from, UInt160 to, ByteString tokenId, object data)
        {
            OnTransfer(from, to, 1, tokenId);
            if (to != null && ContractManagement.GetContract(to) != null)
                Contract.Call(to, "onNEP11Payment", CallFlags.All, from, 1, tokenId, data);
        }

        private static void UpdateTotalSupply(BigInteger increment)
        {
            var key = new byte[] { Prefix_TotalSupply };
            var amount = (BigInteger)Storage.Get(key);
            amount += increment;
            Storage.Put(key, amount);
        }

        private static bool UpdateBalance(UInt160 owner, ByteString tokenId, BigInteger increment)
        {
            StorageMap accountMap = new(Storage.CurrentContext, Prefix_Account);
            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_Balance);

            var balance = (BigInteger)balanceMap.Get(owner);

            balance += increment;

            if (balance < 0)
                return false;
            if (balance.IsZero)
                balanceMap.Delete(owner);
            else
                balanceMap.Put(owner, balance);

            var key = owner + tokenId;

            if (increment > 0)
                accountMap.Put(key, 0);
            else
                accountMap.Delete(key);

            return true;
        }


        [Safe]
        public static UInt160 GetOwner()
        {
            var currentOwner = Storage.Get(new[] { Prefix_Owner });

            if (currentOwner == null)
                return InitialOwner;

            return (UInt160)currentOwner;
        }

       

        public static void SetOwner(UInt160 account)
        {
            if (IsOwner() == false)
                throw new InvalidOperationException("No Authorization!");
            if (account == null || account.IsValid == false)
                throw new InvalidOperationException("Account is invalid!");
            Storage.Put(new[] { Prefix_Owner }, account);
            OnSetNewOwner(account);
        }
        public static bool Verify() => IsOwner();

         private static bool IsOwner() =>
            Runtime.CheckWitness(GetOwner());

        public static void Update(ByteString nefFile, string manifest)
        {
            if (IsOwner() == false)
                throw new InvalidOperationException("No Authorization!");
            ContractManagement.Update(nefFile, manifest);
        }

        public static void Destroy()
        {
            if (IsOwner() == false)
                throw new InvalidOperationException("No Authorization!");
            ContractManagement.Destroy();
        }
    }
}
