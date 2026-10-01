using System;
using System.Collections.Generic;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 // Saved remote storage is data, not a live inventory being opened by a player.
 // Inventory.Load instantiates item prefabs and invokes third-party migrations
 // (e.g. Epic Loot's Inventory.Load postfix), changing even unrelated records.
 // Materialize plain ItemData instead; retain every persisted field and attach
 // only the registered prefab's shared definition for the existing stock/debit code.
 internal static class OfflineInventory {
  internal static void Load(Inventory inventory,byte[] bytes){
   var items=new List<ItemDrop.ItemData>();
   if(bytes.Length>0){
    var slots=new HashSet<(int,int)>();
    foreach(var saved in InventoryRoundTrip.Read(bytes,s=>s.GetStableHashCode())){
     if(saved.X<0||saved.Y<0||!slots.Add((saved.X,saved.Y)))throw new InvalidOperationException("Invalid or duplicate saved item slot");
     var prefab=ObjectDB.instance?ObjectDB.instance.GetItemPrefab(saved.Prefab):null;
     var drop=prefab?prefab.GetComponent<ItemDrop>():null;
     if(!drop||drop.m_itemData?.m_shared==null)throw new InvalidOperationException("Unknown saved item prefab "+saved.Prefab);
     items.Add(new ItemDrop.ItemData{
      m_dropPrefab=prefab,m_shared=drop.m_itemData.m_shared,
      m_stack=saved.Stack,m_durability=saved.LoadedDurability,m_gridPos=new Vector2i(saved.X,saved.Y),
      m_quality=saved.Quality,m_variant=saved.Variant,m_worldLevel=saved.World,
      m_equipped=saved.Equipped,m_pickedUp=saved.PickedUp,m_cheated=saved.Cheated,
      m_crafterID=saved.Crafter,m_crafterName=saved.Name,
      m_customData=new Dictionary<string,string>(saved.Data,StringComparer.Ordinal)
     });
    }
   }
   // Publish atomically after all records resolve. No AddItem/Changed hooks or
   // stack merging during a read; the caller updates its index afterwards.
   var target=inventory.GetAllItems();target.Clear();target.AddRange(items);
  }
 }
}
