using Neo;
using Neo.SmartContract.Framework.Services;

namespace NFT;

public class NFTTokenState
{
    public UInt160 Owner;
    public string Name;

    public string Description;
    public string Image;

    public bool IsOwner() =>
        Runtime.CheckWitness(Owner);
}
