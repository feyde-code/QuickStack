using UnityEngine.Scripting;

[Preserve]
public class NetPackageVehicleUpdateBag : NetPackage
{
    public NetPackageVehicleUpdateBag Setup(int entityID, ItemStack[] _items)
    {
        //Log.Out("NetPackageVehicleUpdateBag-Setup START");
        this.items = _items;
        this.lootEntityId = entityID;

        return this;
    }

    public override void read(PooledBinaryReader _br)
    {
        //Log.Out("NetPackageVehicleUpdateBag-read START");
        this.items = GameUtils.ReadItemStack(_br);
        for (int i = 0; i < this.items.Length; i++)
        {
            if (!this.items[i].IsEmpty())
            {
                //Log.Out("NetPackageVehicleUpdateBag-read this.items[" + i + "].GetItemName: " + this.items[i].itemValue.ItemClass.GetItemName());
                //Log.Out("NetPackageVehicleUpdateBag-read this.items[" + i + "].count: " + this.items[i].count);
            }
        }
        this.lootEntityId = _br.ReadInt32();
    }

    public override void write(PooledBinaryWriter _bw)
    {
        //Log.Out("NetPackageVehicleUpdateBag-write START");
        base.write(_bw);
        _bw.Write((ushort)this.items.Length);
        for (int i = 0; i < this.items.Length; i++)
        {
            this.items[i].Write(_bw);
            if (!this.items[i].IsEmpty())
            {
                //Log.Out("NetPackageVehicleUpdateBag-write this.items[" + i + "].GetItemName: " + this.items[i].itemValue.ItemClass.GetItemName());
                //Log.Out("NetPackageVehicleUpdateBag-write this.items[" + i + "].count: " + this.items[i].count);
            }
        }
        _bw.Write(this.lootEntityId);
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        //Log.Out("NetPackageVehicleUpdateBag-ProcessPackage START");
        if (_world == null)
        {
            //Log.Out("NetPackageVehicleUpdateBag-ProcessPackage 1");
            return;
        }

        EntityVehicle entity = GameManager.Instance.World.GetEntity(this.lootEntityId) as EntityVehicle;
        if (entity != null)
        {
            //Log.Out("NetPackageVehicleUpdateBag-ProcessPackage FOUND VEHICLE");
            entity.bag.SetSlots(this.items);
        }
        else
        {
            //Log.Out("NetPackageVehicleUpdateBag-ProcessPackage 2");
        }
    }

    public override int GetLength()
    {
        return 16;
    }

    private int lootEntityId;
    private ItemStack[] items;
}
