using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NFTAccounts
{
    public enum Reaction : byte
    {
        None,
        Kind,
        Funny,
        Sad,
        Angry
    }

 

    public class Post
    {
        public ByteString postId;
        public string content;
        public string image;
        public UInt160 prompter;
        public BigInteger kind;
        public BigInteger funny;
        public BigInteger sad;
        public BigInteger angry;
        public Map<ByteString,Reaction> reactions;
        public ByteString replyPostId;
    }

    public class Account
    {
        public ByteString accountId;
        public UInt160 nftScriptHash;
        public ByteString tokenId;
        public Map<Reaction,BigInteger> personality;

        public BigInteger popularity;

        public Map<ByteString,Post> posts;
        public BigInteger followersCount;
        public BigInteger followingCount;

        public Map<ByteString,bool> followers;
        public Map<ByteString,bool> following; 
    }

    [DisplayName("Gabriel.NFTAccountsContract")]
    [ManifestExtra("Author", "Gabriel Antony Xaviour")]
    [ManifestExtra("Email", "gabrielantony56@gmail.com")]
    [ManifestExtra("Description", "Describe your contract...")]
    public class NFTAccountsContract : SmartContract
    {
        private const byte Prefix_Accounts=0x01;
        private const byte Prefix_TokenId = 0x02;

        private const byte Prefix_ContractOwner = 0xFF;


        public delegate void OnAccountInitializedDelegate(UInt160 nftScriptHash, ByteString tokenId,BigInteger salt, BigInteger kind, BigInteger funny, BigInteger sad, BigInteger angry);
        public delegate void OnPostedDelegate(ByteString postId,ByteString accountId, string content,string image);
        public delegate void OnFollowedDelegate(ByteString followerAccountId,ByteString followingAccountId,BigInteger followersCount,BigInteger followingCount);
        public delegate void OnUnfollowedDelegate(ByteString unfollowingAccountId,ByteString unfollowedAccountId);
        public delegate void OnReactedDelegate(ByteString postId, ByteString reactedAccountId,ByteString receivedAccountId,Reaction reaction);
        public delegate void OnOracleReturnedDelegate(string url, object userData, OracleResponseCode code, string result);


        [DisplayName("OracleReturned")]
        public static event OnOracleReturnedDelegate OnOracleReturned = default!;

        [DisplayName("AccountInitialized")]
        public static event OnAccountInitializedDelegate OnAccountInitialized = default!;

        [DisplayName("Posted")]
        public static event OnPostedDelegate OnPosted = default!;

        [DisplayName("Followed")]
        public static event OnFollowedDelegate OnFollowed = default!;

        [DisplayName("UnFollowed")]
        public static event OnUnfollowedDelegate OnUnfollowed = default!;

        [DisplayName("Reacted")]
        public static event OnReactedDelegate OnReacted = default!;

       public static ByteString GetAccountId(UInt160 nftScriptHash,ByteString tokenId)
        {
            return nftScriptHash.Concat(tokenId);
        }

        public static ByteString GetPostId(string content)
        {
            return CryptoLib.Ripemd160(CryptoLib.Sha256(content));
        }

        public static ByteString CreateAccount(UInt160 nftScriptHash,ByteString tokenId){

            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            ByteString accountId=GetAccountId(nftScriptHash,tokenId);

            if(accounts.Get(accountId)!=null)
            {
                throw new Exception("Account already exists");
            }

            UInt160 nftOwner=GetOwner(nftScriptHash,tokenId);

            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("not owner");
            }

            Account account = new Account();
            account.accountId=accountId;
            account.nftScriptHash=nftScriptHash;
            account.tokenId=tokenId;
            account.personality=new Map<Reaction, BigInteger>();
            BigInteger salt = Runtime.GetRandom();

            BigInteger kind = salt%100;
            salt =(salt-kind) / 100;
            BigInteger funny = salt%100;
            salt = (salt-funny) / 100;
            BigInteger sad =  salt%100;
            salt = (salt-sad) / 100;
            BigInteger angry =  salt%100;
            salt = (salt-angry) / 100;

            account.personality[Reaction.Kind]=kind;
            account.personality[Reaction.Funny]=funny;
            account.personality[Reaction.Sad]=sad;
            account.personality[Reaction.Angry]=angry;

            account.popularity=0;
            account.posts=new Map<ByteString, Post>();

            account.followersCount=0;
            account.followingCount=0;

            account.followers=new Map<ByteString, bool>();
            account.following=new Map<ByteString, bool>();

            accounts.Put(accountId, StdLib.Serialize(account));
            OnAccountInitialized(nftScriptHash,tokenId,salt,kind,funny,sad,angry);

            return account.accountId;
        }

        public static string[] ConvertByteArrayToString(byte[] data)
        {
            return (string[])StdLib.JsonDeserialize((ByteString)data);
        }


        public static ByteString Post(ByteString accountId,string prompt,ByteString replyPostId)
        {

            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(accountId)==null)
            {
                throw new Exception("Account does not exist");
            }

            Account account = (Account)StdLib.Deserialize(accounts.Get(accountId));
            UInt160 nftOwner=GetOwner(account.nftScriptHash, account.tokenId);
            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("Unauthorized");
            }

            string callback = "callback"; // callback method
            ByteString postId=GetPostId(prompt);
            object[] userdata = new object[]{accountId,postId,replyPostId} ; // arbitrary type
            long gasForResponse = Oracle.MinimumResponseFee;

            string url=GetApiUrl(prompt);
            string filter="$.data.post[*].value";

            Oracle.Request(url, filter, callback, userdata, gasForResponse);
            return postId;
        }


        public static string GetApiUrl(string prompt)
        {
            return "https://nftgram.in/api/generate?prompt="+prompt;
        }


        public static void Callback(string requestedUrl, object[] userData, OracleResponseCode oracleResponseCode, byte[] result)
        {
            if (Runtime.CallingScriptHash != Oracle.Hash) throw new Exception("Unauthorized!");

            if (oracleResponseCode != OracleResponseCode.Success) throw new Exception("Error Code: "+(byte)oracleResponseCode);
            string[] responseData=ConvertByteArrayToString(result);
            
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            Account account = (Account)StdLib.Deserialize(accounts.Get((ByteString)userData[0]));

            UInt160 nftScriptHash=account.nftScriptHash;
            ByteString tokenId=account.tokenId;
            UInt160 prompter=GetOwner(nftScriptHash, tokenId);

            if(account.posts.HasKey((ByteString)userData[1]))
            {
                throw new Exception("Post already exists");
            }

            Post post = new Post();
            post.postId = (ByteString)userData[1];
            post.content = responseData[0];
            post.image=responseData[1];
            post.prompter = prompter;
            post.kind = 0;
            post.funny = 0;
            post.sad = 0;
            post.angry = 0;
            post.replyPostId=(ByteString)userData[2];
            post.reactions = new Map<ByteString, Reaction>();

            account.posts[(ByteString)userData[1]]=post;

            accounts.Put((ByteString)userData[0], StdLib.Serialize(account));

            OnPosted((ByteString)userData[1],(ByteString)userData[0], responseData[0],responseData[1]);
        }

      

        public static void React(ByteString postId,ByteString userAccountId,ByteString receiverAccountId, Reaction reaction)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(userAccountId)==null)
            {
                throw new Exception("User Account does not exist");
            }
            if(accounts.Get(receiverAccountId)==null)
            {
                throw new Exception("Receiver Account does not exist");
            }
            Account userAccount = (Account)StdLib.Deserialize(accounts.Get(userAccountId));
            Account receiverAccount = (Account)StdLib.Deserialize(accounts.Get(receiverAccountId));
            UInt160 nftOwner=GetOwner(userAccount.nftScriptHash,userAccount.tokenId);

            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("not owner");
            }
            
            receiverAccount.posts[postId].reactions[userAccountId]=reaction;
            if(reaction==Reaction.Kind)
            {
                receiverAccount.posts[postId].kind+=1;
                receiverAccount.personality[Reaction.Kind]+=1;
            }
            else if(reaction==Reaction.Funny)
            {
                receiverAccount.posts[postId].funny+=1;
                receiverAccount.personality[Reaction.Funny]+=1;
            }
            else if(reaction==Reaction.Sad)
            {
                receiverAccount.posts[postId].sad+=1;
                receiverAccount.personality[Reaction.Sad]+=1;
            }else if(reaction==Reaction.Angry)
            {
                receiverAccount.posts[postId].angry+=1;
                receiverAccount.personality[Reaction.Angry]+=1;
            }else{
                receiverAccount.popularity-=1;
            }
            receiverAccount.popularity+=1;

            accounts.Put(receiverAccountId, StdLib.Serialize(receiverAccount));
            
            
            OnReacted(postId, userAccountId,receiverAccountId, reaction);
            
        }

        public static void Follow(ByteString followerAccountId,ByteString followingAccountId)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);

            if(accounts.Get(followerAccountId)==null)
            {
                throw new Exception("Follower Account does not exist");
            }
            if(accounts.Get(followingAccountId)==null)
            {
                throw new Exception("Following Account does not exist");
            }
            Account followerAccount = (Account)StdLib.Deserialize(accounts.Get(followerAccountId));
            Account followingAccount = (Account)StdLib.Deserialize(accounts.Get(followingAccountId));

            UInt160 nftOwner=GetOwner(followerAccount.nftScriptHash,followerAccount.tokenId);


            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("not owner");
            }

            if(followingAccount.followers.HasKey(followerAccountId))
            {
                if(followingAccount.followers[followerAccountId]==true)
                {
                    throw new Exception("already following");
                }
            }

 
           
            followingAccount.followers[followerAccountId]=true;
            followingAccount.followersCount+=1;
            followingAccount.popularity+=1;

            followerAccount.following[followingAccountId]=true;
            followerAccount.followingCount+=1;

            accounts.Put(followerAccountId, StdLib.Serialize(followerAccount));
            accounts.Put(followingAccountId, StdLib.Serialize(followingAccount));


            OnFollowed(followerAccountId, followingAccountId,followingAccount.followersCount,followerAccount.followingCount);
        }

        public static void UnFollow(ByteString unfollowerAccountId,ByteString unfollowingAccountId)
        {
           StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(unfollowerAccountId)==null)
            {
                throw new Exception("Unfollower Account does not exist");
            }
            if(accounts.Get(unfollowingAccountId)==null)
            {
                throw new Exception("Unfollowing Account does not exist");
            }
            Account unfollowerAccount = (Account)StdLib.Deserialize(accounts.Get(unfollowerAccountId));
            Account unfollowingAccount = (Account)StdLib.Deserialize(accounts.Get(unfollowingAccountId));

            UInt160 nftOwner=GetOwner(unfollowerAccount.nftScriptHash,unfollowerAccount.tokenId);

            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("not owner");
            }

            if(!unfollowingAccount.followers.HasKey(unfollowerAccountId))
            {

                throw new Exception("not following");
            }

            if(unfollowingAccount.followers[unfollowerAccountId]==false)
            {
                throw new Exception("not following");
            }
            unfollowingAccount.followers[unfollowerAccountId]=false;
            unfollowingAccount.followersCount-=1;
            unfollowingAccount.popularity-=1;

            unfollowerAccount.following[unfollowingAccountId]=false;
            unfollowerAccount.followingCount-=1;

            accounts.Put(unfollowerAccountId, StdLib.Serialize(unfollowerAccount));
            accounts.Put(unfollowingAccountId, StdLib.Serialize(unfollowingAccount));


            OnUnfollowed(unfollowerAccountId, unfollowingAccountId);
        }

        public static void DeleteAccount(ByteString accountId)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(accountId)==null)
            {
                throw new Exception("Account does not exist");
            }
            Account account = (Account)StdLib.Deserialize(accounts.Get(accountId));
            UInt160 nftOwner=GetOwner(account.nftScriptHash,account.tokenId);

            if (!Runtime.CheckWitness(nftOwner))
            {
                throw new Exception("not owner");
            }
            accounts.Delete(accountId);
        }

        // Getters

        public static UInt160 GetOwner(UInt160 nftScriptHash, ByteString tokenId)
        {
            UInt160 owner=(UInt160)Contract.Call(nftScriptHash, "ownerOf", CallFlags.All, tokenId);
            return owner;
        }

        public static bool Verify(UInt160 nftScriptHash, ByteString tokenId)
        {
            UInt160 owner=(UInt160)Contract.Call(nftScriptHash, "ownerOf", CallFlags.All, tokenId);
            return Runtime.CheckWitness(owner);
        }
        public static Account GetAccount(ByteString accountId)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(accountId)==null)
            {
                throw new Exception("Account does not exist");
            }
            Account account = (Account)StdLib.Deserialize(accounts.Get(accountId));
            return account;
        }

        public static Post GetPost(ByteString accountId,ByteString postId)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(accountId)==null)
            {
                throw new Exception("Account does not exist");
            }
            Account account = (Account)StdLib.Deserialize(accounts.Get(accountId));
            if(!account.posts.HasKey(postId))
            {
                throw new Exception("Post does not exist");
            }
            Post post = account.posts[postId];
            return post;
        }

        public static bool isFollowing(ByteString followerAccount,ByteString followingAccount)
        {
            StorageMap accounts = new(Storage.CurrentContext, Prefix_Accounts);
            if(accounts.Get(followerAccount)==null)
            {
                throw new Exception("Follower Account does not exist");
            }
            if(accounts.Get(followingAccount)==null)
            {
                throw new Exception("Following Account does not exist");
            }
            Account follower = (Account)StdLib.Deserialize(accounts.Get(followerAccount));
            Account following = (Account)StdLib.Deserialize(accounts.Get(followingAccount));
            if(following.followers.HasKey(followerAccount)==false)
            {
                return false;
            }
            return following.followers[followerAccount];
        }

       [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;

            var key = new byte[] { Prefix_ContractOwner };
            var Tx=(Transaction)Runtime.ScriptContainer;
            
            Storage.Put(Storage.CurrentContext, key, Tx.Sender);
        }

        public static void Update(ByteString nefFile, string manifest)
        {
            var key = new byte[] { Prefix_ContractOwner };
            var contractOwner = (UInt160)Storage.Get(Storage.CurrentContext, key);
            var Tx=(Transaction)Runtime.ScriptContainer;

             

            if (contractOwner!=Tx.Sender)
            {
                throw new Exception("Only the contract owner can update the contract");
            }

            ContractManagement.Update(nefFile, manifest, null);
        }
    }
}
