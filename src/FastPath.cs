using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static class FastPath {
  sealed class Source:FastPathCoreSource {
   internal string SourceKey;internal Container Container;internal Inventory Inventory;internal ZDO Data;internal long InitialOwner;internal InventoryDelta Delta;
   internal bool Held;
   internal override string Key=>SourceKey;
   internal override long Owner=>InitialOwner;
   internal override long CurrentOwner=>Data?.GetOwner()??0;
   internal override uint Revision=>Data?.DataRevision??0;
   internal override bool IsValid=>Data!=null&&Data.IsValid()&&Container&&Inventory!=null;
   internal override byte[] Bytes=>InventoryBytes(Inventory);
   internal override string DebitDescription=>string.Join(",",Delta?.Parts.Select(part=>part.Item.m_dropPrefab.name+"="+part.Amount)??Enumerable.Empty<string>());
   internal override void AcquireHold(){
    if(!IsValid||Holds(Data)||heldInventories.ContainsKey(Inventory))throw new FastPathFenceException("source became reserved: "+SourceKey);
    heldSources.Add(Data.m_uid,this);heldInventories.Add(Inventory,this);Held=true;Integrations.Block(Inventory,true);R.Set(Container,"m_inUse",true);
   }
   internal override void ApplyDebit(){Transport.InternalMutation++;try{Delta.Apply();}finally{Transport.InternalMutation--;}}
   internal override void Save(){Transport.InternalMutation++;try{R.Call(Container,"Save");}finally{Transport.InternalMutation--;}}
   internal override void RestoreDebit(){Transport.InternalMutation++;try{Delta.Restore();}finally{Transport.InternalMutation--;}}
   internal override void ReleaseHold(){if(!Held)return;Integrations.Block(Inventory,false);R.Set(Container,"m_inUse",false);heldInventories.Remove(Inventory);heldSources.Remove(Data.m_uid);Held=false;}
  }
  sealed class PlayerAdapter:FastPathCorePlayer {
   internal Player Value;internal ItemDrop.ItemData Tool;internal InventoryDelta Delta;
   internal override byte[] Bytes=>InventoryBytes(Value.GetInventory());
   internal override void AcquireHold(){lockedPlayerInventory=Value.GetInventory();lockedTool=Tool;}
   internal override void ApplyDebit(){Transport.InternalMutation++;try{Delta.Apply();}finally{Transport.InternalMutation--;}}
   internal override void RestoreDebit(){Transport.InternalMutation++;try{Delta.Restore();}finally{Transport.InternalMutation--;}}
   internal override void ReleaseHold(){if(lockedPlayerInventory==Value.GetInventory())lockedPlayerInventory=null;if(lockedTool==Tool)lockedTool=null;}
  }
  sealed class Transaction {internal Actions.Pending Pending;internal PlayerAdapter Player;}

  static readonly Dictionary<Inventory,Source> heldInventories=new Dictionary<Inventory,Source>();
  static readonly Dictionary<ZDOID,Source> heldSources=new Dictionary<ZDOID,Source>();
  static readonly FastPathCore engine=new FastPathCore(message=>Plugin.Log.LogWarning("[RSN] "+message));
  static Inventory lockedPlayerInventory;static ItemDrop.ItemData lockedTool;static Transaction current;

  internal static bool Running=>engine.Running;
  internal static bool Active=>current!=null;
  internal static bool Holds(ZDO data,string except=null)=>data!=null&&heldSources.ContainsKey(data.m_uid);
  internal static bool Locked(Inventory inventory)=>inventory!=null&&(heldInventories.ContainsKey(inventory)||lockedPlayerInventory==inventory);
  internal static bool LockedItem(ItemDrop.ItemData item)=>item!=null&&(item==lockedTool||heldInventories.Keys.Any(inventory=>inventory.ContainsItem(item)));
  internal static bool Blocks(Player player)=>current!=null&&current.Player.Value==player&&engine.Blocks(current.Player);
  internal static bool Owns(Player player)=>current!=null&&current.Player.Value==player;
  internal static bool NativeReady(Player player)=>Owns(player)&&engine.NativeReady(current.Player);
  internal static bool ConsumeNativePlacement(Player player)=>Owns(player)&&engine.ConsumeNativePlacement(current.Player);

  internal static bool TryBuild(Player player,Piece piece,Operation operation,Core core,List<Debit> preview){
   if(!Eligible(player,piece,operation,core,preview,out var plan,out var sources))return false;
   var pending=new Actions.Pending{Op=operation,Player=player,Piece=piece,Tool=(ItemDrop.ItemData)R.Call(player,"GetRightItem",Type.EmptyTypes)};
   var ghost=R.Get<GameObject>(player,"m_placementGhost");pending.Position=ghost.transform.position;pending.Rotation=ghost.transform.rotation;
   var playerAdapter=new PlayerAdapter{Value=player,Tool=pending.Tool,Delta=new InventoryDelta(player.GetInventory(),plan.Where(debit=>debit.Source=="player"),false)};
   foreach(var source in sources)source.Delta=new InventoryDelta(source.Inventory,plan.Where(debit=>debit.Source==source.SourceKey),true);
   current=new Transaction{Pending=pending,Player=playerAdapter};
   bool handled=engine.TryBuild(playerAdapter,sources.Cast<FastPathCoreSource>().ToList(),()=>{
    pending.Output=false;Actions.Active=pending;
    try{return player.TryPlacePiece(piece)||pending.Output;}finally{Actions.Active=null;}
   },()=>{pending.Output=true;Actions.FinishBuild(pending);});
   if(!engine.Running){current=null;Actions.Active=null;}
   return handled;
  }

  static bool Eligible(Player player,Piece piece,Operation operation,Core core,List<Debit> preview,out List<Debit> plan,out List<Source> sources){
   plan=null;sources=null;
   if(Plugin.OwnerFastPath==null||!Plugin.OwnerFastPath.Value||Running||Actions.Waiting!=null||Actions.Active!=null)return false;
   if(!player||player!=Player.m_localPlayer||!piece||operation==null||core==null||preview==null||!preview.Any(debit=>debit.Source!="player"&&debit.Amount>0))return false;
   if(Actions.LocalBuildMaterials(player,piece,Player.RequirementMode.CanBuild))return false;
   var tool=(ItemDrop.ItemData)R.Call(player,"GetRightItem",Type.EmptyTypes);if(Transport.Locked(player.GetInventory())||Transport.LockedItem(tool))return false;
   core.Scan();var selected=preview.Where(debit=>debit.Source!="player"&&debit.Amount>0).Select(debit=>debit.Source).Distinct(StringComparer.Ordinal).ToArray();var stock=Stockroom.Snapshot(player.GetInventory(),"player",operation.Needs,false);var byKey=new Dictionary<string,Container>(StringComparer.Ordinal);
   foreach(string key in selected){
    var container=core.Pool.FirstOrDefault(candidate=>candidate&&R.Valid(R.View(candidate))&&R.Key(R.View(candidate).GetZDO().m_uid)==key);
    if(!container||container.GetComponent<UnloadedReplica>()||!R.View(container).IsOwner()||!Access.Container(container,player.GetPlayerID(),core,out _,ownLease:false)||Holds(R.View(container).GetZDO())||Transport.Locked(container.GetInventory())||Integrations.IsBusy(container.GetInventory()))return false;
    var data=R.View(container).GetZDO();if(data.GetOwner()!=ZNet.GetUID()||data.GetInt(ZDOVars.s_inUse)!=0||container.IsInUse())return false;
    stock.AddRange(GatewayRuntime.Local(core,player.GetPlayerID(),container,Stockroom.Snapshot(container.GetInventory(),key,operation.Needs,true)));byKey.Add(key,container);
   }
   plan=Planner.Plan(operation.Needs,stock,true);if(plan==null||!plan.Any(debit=>debit.Source!="player"))return false;if(!GatewayRuntime.ValidatePlan(operation,plan))return false;
   var actual=plan.Where(debit=>debit.Source!="player").Select(debit=>debit.Source).Distinct(StringComparer.Ordinal).ToArray();if(!new HashSet<string>(actual,StringComparer.Ordinal).SetEquals(selected))return false;
   sources=new List<Source>();foreach(string key in actual){var container=byKey[key];var data=R.View(container).GetZDO();sources.Add(new Source{SourceKey=key,Container=container,Inventory=container.GetInventory(),Data=data,InitialOwner=data.GetOwner()});}
   return true;
  }

  internal static void Tick(){engine.Tick();if(!engine.Running){current=null;Actions.Active=null;}}
  internal static void ClearWorld(){engine.Clear();heldInventories.Clear();heldSources.Clear();lockedPlayerInventory=null;lockedTool=null;current=null;Actions.Active=null;}
  internal static byte[] InventoryBytes(Inventory inventory){var package=new ZPackage();inventory.Save(package);return package.GetArray();}
 }
}
