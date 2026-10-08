using System;
using System.Collections.Generic;
using System.Linq;
#if !FAST_PATH_RUNTIME_TESTS
using UnityEngine;
using RunicStorageNetwork.Logic;
#endif

 #if FAST_PATH_RUNTIME_TESTS
namespace RunicStorageNetwork {
 internal sealed class FastPathRuntimeItem {
  internal string Name;internal int Stack;internal int Quality;internal string Metadata;
  internal FastPathRuntimeItem Clone()=>new FastPathRuntimeItem{Name=Name,Stack=Stack,Quality=Quality,Metadata=Metadata};
 }
 internal sealed class FastPathRuntimeInventory {
  internal readonly List<FastPathRuntimeItem> Items=new List<FastPathRuntimeItem>();
  internal bool FailRemove;
  internal byte[] Bytes(){return System.Text.Encoding.UTF8.GetBytes(string.Join("|",Items.OrderBy(item=>item.Name).ThenBy(item=>item.Quality).Select(item=>item.Name+":"+item.Stack+":"+item.Quality+":"+item.Metadata)));}
  internal FastPathRuntimeInventory Clone(){var copy=new FastPathRuntimeInventory();foreach(var item in Items)copy.Items.Add(item.Clone());return copy;}
 }
 internal sealed class FastPathRuntimeChest {
  internal string Key;internal long Owner=1;internal int Revision;internal readonly FastPathRuntimeInventory Inventory=new FastPathRuntimeInventory();
  internal bool Busy,InUse,IntegrationBlocked,FailSave,FailRelease,GatewayAllowed=true;internal string PersistentLease="";internal int RemoteRefusals;
  internal byte[] Bytes()=>Inventory.Bytes();
 }
 internal sealed class FastPathRuntimePlayer {
  internal readonly FastPathRuntimeInventory Inventory=new FastPathRuntimeInventory();internal bool BuildAllowed=true;internal bool ToolLocked,PlacementRefused;internal int Costs,Outputs;
 }
 internal sealed class FastPathRuntime {
  sealed class Held {internal FastPathRuntimeChest Chest;internal byte[] Before;internal bool Debited;}
  readonly Dictionary<string,Held> held=new Dictionary<string,Held>(StringComparer.Ordinal);FastPathRuntimePlayer blocked;FastPathRuntimePlayer active;byte[] playerBefore;bool compensation;bool cleanup;bool output;
  internal bool TryBuild(FastPathRuntimePlayer player,IReadOnlyDictionary<string,int> needs,IReadOnlyList<FastPathRuntimeChest> chests,Action reentrant=null){
   if(blocked!=null||active!=null||!player.BuildAllowed||player.ToolLocked||chests.Any(chest=>chest.Busy||chest.InUse||chest.Owner!=1||!chest.GatewayAllowed))return false;
   active=player;output=false;playerBefore=player.Inventory.Bytes();var snapshots=chests.ToDictionary(chest=>chest.Key,chest=>chest.Bytes(),StringComparer.Ordinal);try{
    foreach(var chest in chests){held[chest.Key]=new Held{Chest=chest,Before=(byte[])snapshots[chest.Key].Clone()};chest.InUse=true;chest.IntegrationBlocked=true;}
    var remaining=needs.ToDictionary(entry=>entry.Key,entry=>entry.Value,StringComparer.Ordinal);foreach(var item in player.Inventory.Items){if(remaining.TryGetValue(item.Name,out var amount)){var take=Math.Min(amount,item.Stack);item.Stack-=take;remaining[item.Name]-=take;}}
    foreach(var chest in chests){foreach(var item in chest.Inventory.Items){if(remaining.TryGetValue(item.Name,out var amount)){var take=Math.Min(amount,item.Stack);if(take>0){item.Stack-=take;remaining[item.Name]-=take;held[chest.Key].Debited=true;if(chest.Inventory.FailRemove)throw new InvalidOperationException("RemoveItem refused");}}}if(remaining.Values.Any(value=>value<0))throw new InvalidOperationException("invalid debit");if(chest.FailSave)throw new InvalidOperationException("save refused");chest.Revision++;}
    if(remaining.Values.Any(value=>value>0))throw new InvalidOperationException("Fresh stock insufficient");
    reentrant?.Invoke();
    if(player.PlacementRefused)throw new InvalidOperationException("placement refused");player.Outputs++;player.Costs++;output=true;Release();return true;
   }catch(Exception){if(output){cleanup=true;return true;}compensation=true;blocked=player;return true;}
  }
  internal void Tick(){if(compensation){try{foreach(var pair in held){var chest=pair.Value.Chest;if(chest.Owner!=1)throw new InvalidOperationException("ownership changed");var before=Parse(pair.Value.Before);chest.Inventory.Items.Clear();chest.Inventory.Items.AddRange(before);chest.Revision++;pair.Value.Debited=false;}active.Inventory.Items.Clear();active.Inventory.Items.AddRange(Parse(playerBefore));compensation=false;Release();blocked=null;active=null;}catch{}}else if(cleanup){try{Release();cleanup=false;active=null;blocked=null;}catch{}}}
  internal bool BuildBlocked(FastPathRuntimePlayer player)=>blocked==player;
  internal bool Holds(string key)=>held.ContainsKey(key);
  internal bool RemoteOpen(FastPathRuntimeChest chest){if(Holds(chest.Key)){chest.RemoteRefusals++;return false;}return true;}
  internal bool OwnershipMove(FastPathRuntimeChest chest,long owner){return !Holds(chest.Key)||owner==chest.Owner;}
  internal bool HasPersistentMarkers=>held.Values.Any(held=>held.Chest.PersistentLease.Length>0);
  void Release(){foreach(var heldSource in held.Values){var chest=heldSource.Chest;if(chest.FailRelease)throw new InvalidOperationException("release refused");chest.InUse=false;chest.IntegrationBlocked=false;}held.Clear();}
  static List<FastPathRuntimeItem> Parse(byte[] bytes){var result=new List<FastPathRuntimeItem>();var text=System.Text.Encoding.UTF8.GetString(bytes);if(text.Length==0)return result;foreach(var row in text.Split('|')){var parts=row.Split(':');result.Add(new FastPathRuntimeItem{Name=parts[0],Stack=int.Parse(parts[1]),Quality=int.Parse(parts[2]),Metadata=parts[3]});}return result;}
 }
}
 #else
namespace RunicStorageNetwork {
 internal static class FastPath {
  sealed class Source {
   internal string Key;internal Container Container;internal Inventory Inventory;internal ZDO Data;internal long Owner;internal uint Revision;
   internal byte[] Before,Expected;internal InventoryDelta Delta;internal bool Applied;
  }
  sealed class Transaction {
   internal Actions.Pending Pending;internal Player Player;internal ItemDrop.ItemData Tool;internal List<Source> Sources;
   internal InventoryDelta PlayerDelta;internal byte[] PlayerBefore,PlayerExpected;internal bool Compensating,Cleaning,Output,NativeCall;
  }
  static readonly Dictionary<Inventory,Source> heldInventories=new Dictionary<Inventory,Source>();
  static readonly Dictionary<ZDOID,Source> heldSources=new Dictionary<ZDOID,Source>();
  static Inventory lockedPlayerInventory;static ItemDrop.ItemData lockedTool;
  static Transaction current;static Player blockedPlayer;

  internal static bool Active=>current!=null;
  internal static bool Holds(ZDO data,string except=null)=>data!=null&&heldSources.TryGetValue(data.m_uid,out var source)&&source.Key!=except;
  internal static bool Locked(Inventory inventory)=>inventory!=null&&(heldInventories.ContainsKey(inventory)||lockedPlayerInventory==inventory);
  internal static bool LockedItem(ItemDrop.ItemData item)=>item!=null&&(item==lockedTool||heldInventories.Keys.Any(inventory=>inventory.ContainsItem(item)));
  internal static bool Blocks(Player player)=>blockedPlayer&&blockedPlayer==player;
  internal static bool Owns(Player player)=>current!=null&&current.Player==player;
  internal static bool ConsumeNativePlacement(Player player){if(current==null||current.Player!=player||!current.NativeCall)return false;current.NativeCall=false;return true;}

  internal static bool TryBuild(Player player,Piece piece,Operation operation,Core core,List<Debit> preview){
   if(!Eligible(player,piece,operation,core,preview,out var plan,out var sources))return false;
   var pending=new Actions.Pending{Op=operation,Player=player,Piece=piece,Tool=(ItemDrop.ItemData)R.Call(player,"GetRightItem",Type.EmptyTypes)};
   var ghost=R.Get<GameObject>(player,"m_placementGhost");pending.Position=ghost.transform.position;pending.Rotation=ghost.transform.rotation;
   var transaction=new Transaction{Pending=pending,Player=player,Tool=pending.Tool,Sources=sources};current=transaction;Actions.Active=pending;
   try {
    Reserve(transaction);
    transaction.PlayerBefore=InventoryBytes(player.GetInventory());
    transaction.PlayerDelta=new InventoryDelta(player.GetInventory(),plan.Where(d=>d.Source=="player"),false);
    foreach(var source in sources)source.Delta=new InventoryDelta(source.Inventory,plan.Where(d=>d.Source==source.Key),true);
    Apply(transaction,plan);
   } catch(Exception error) {
    Plugin.Debug(operation.Id+" fast path transaction: "+error.Message);
    if(transaction.Output){transaction.Cleaning=true;return true;}
    transaction.Compensating=true;TryCompensate(transaction);
   }
   return true;
  }

  static bool Eligible(Player player,Piece piece,Operation operation,Core core,List<Debit> preview,out List<Debit> plan,out List<Source> sources){
   plan=null;sources=null;
   if(Plugin.OwnerFastPath==null||!Plugin.OwnerFastPath.Value||current!=null||blockedPlayer!=null||Actions.Waiting!=null||Actions.Active!=null)return false;
   if(!player||player!=Player.m_localPlayer||!piece||operation==null||core==null||preview==null||!preview.Any(d=>d.Source!="player"&&d.Amount>0))return false;
   if(Actions.LocalBuildMaterials(player,piece,Player.RequirementMode.CanBuild))return false;
   if(Transport.Locked(player.GetInventory())||Transport.LockedItem((ItemDrop.ItemData)R.Call(player,"GetRightItem",Type.EmptyTypes)))return false;
   core.Scan();var selected=preview.Where(d=>d.Source!="player"&&d.Amount>0).Select(d=>d.Source).Distinct(StringComparer.Ordinal).ToArray();var stock=Stockroom.Snapshot(player.GetInventory(),"player",operation.Needs,false);
   var byKey=new Dictionary<string,Container>(StringComparer.Ordinal);
   foreach(string key in selected){
    var container=core.Pool.FirstOrDefault(candidate=>candidate&&R.Valid(R.View(candidate))&&R.Key(R.View(candidate).GetZDO().m_uid)==key);
    if(!container||container.GetComponent<UnloadedReplica>()||!R.View(container).IsOwner()||!Access.Container(container,player.GetPlayerID(),core,out _,ownLease:false)||FastPath.Holds(R.View(container).GetZDO())||Transport.Locked(container.GetInventory())||Integrations.IsBusy(container.GetInventory()))return false;
    var data=R.View(container).GetZDO();if(data.GetOwner()!=ZNet.GetUID()||data.GetInt(ZDOVars.s_inUse)!=0||container.IsInUse())return false;
    var local=GatewayRuntime.Local(core,player.GetPlayerID(),container,Stockroom.Snapshot(container.GetInventory(),key,operation.Needs,true));stock.AddRange(local);byKey.Add(key,container);
   }
   plan=Planner.Plan(operation.Needs,stock,true);if(plan==null||!plan.Any(d=>d.Source!="player"))return false;
   if(!GatewayRuntime.ValidatePlan(operation,plan))return false;
   var actual=plan.Where(d=>d.Source!="player").Select(d=>d.Source).Distinct(StringComparer.Ordinal).ToArray();if(!new HashSet<string>(actual,StringComparer.Ordinal).SetEquals(selected))return false;
   sources=new List<Source>();foreach(string key in actual){var c=byKey[key];var z=R.View(c).GetZDO();var bytes=InventoryBytes(c.GetInventory());sources.Add(new Source{Key=key,Container=c,Inventory=c.GetInventory(),Data=z,Owner=z.GetOwner(),Revision=z.DataRevision,Before=bytes,Expected=(byte[])bytes.Clone()});}
   return true;
  }

  static void Reserve(Transaction transaction){
   lockedPlayerInventory=transaction.Player.GetInventory();lockedTool=transaction.Tool;
   foreach(var source in transaction.Sources){
    if(Holds(source.Data)||heldInventories.ContainsKey(source.Inventory))throw new InvalidOperationException("source became reserved");
    heldSources.Add(source.Data.m_uid,source);heldInventories.Add(source.Inventory,source);Integrations.Block(source.Inventory,true);R.Set(source.Container,"m_inUse",true);
   }
  }
  static void Apply(Transaction transaction,List<Debit> plan){
   transaction.NativeCall=false;ApplySources(transaction,plan);ApplyPlayer(transaction,plan);
   transaction.NativeCall=true;bool built=false;try{built=transaction.Player.TryPlacePiece(transaction.Pending.Piece);}finally{transaction.NativeCall=false;}
   if(!built&&!transaction.Pending.Output)throw new InvalidOperationException("placement refused");
   transaction.Output=true;transaction.Pending.Output=true;Actions.FinishBuild(transaction.Pending);Release(transaction);Actions.Active=null;current=null;Plugin.Debug(transaction.Pending.Op.Id+" fast path output");
  }
  static void ApplySources(Transaction transaction,List<Debit> plan){
   foreach(var source in transaction.Sources){Check(source);source.Applied=true;Transport.InternalMutation++;try{source.Delta.Apply();Save(source);}catch{source.Expected=InventoryBytes(source.Inventory);throw;}finally{Transport.InternalMutation--;}}
  }
  static void ApplyPlayer(Transaction transaction,List<Debit> plan){
   CheckPlayer(transaction);Transport.InternalMutation++;try{transaction.PlayerDelta.Apply();transaction.PlayerExpected=InventoryBytes(transaction.Player.GetInventory());}finally{Transport.InternalMutation--;}
  }
  static void TryCompensate(Transaction transaction){
   blockedPlayer=transaction.Player;transaction.Compensating=true;
   try{Rollback(transaction);if(transaction.Sources.Any(source=>source.Applied)||(transaction.PlayerDelta?.Applied??false))return;transaction.Compensating=false;Release(transaction);Actions.Active=null;current=null;blockedPlayer=null;}
   catch(Exception error){Plugin.Debug(transaction.Pending.Op.Id+" compensation pending: "+error.Message);}
  }
  static void Rollback(Transaction transaction){
   if(transaction.PlayerDelta?.Applied==true){CheckPlayer(transaction);Transport.InternalMutation++;try{transaction.PlayerDelta.Restore();transaction.PlayerExpected=InventoryBytes(transaction.Player.GetInventory());}finally{Transport.InternalMutation--;}}
   for(int index=transaction.Sources.Count-1;index>=0;index--){var source=transaction.Sources[index];if(!source.Applied)continue;Check(source);Transport.InternalMutation++;try{source.Delta.Restore();Save(source);source.Applied=false;}catch{source.Expected=InventoryBytes(source.Inventory);throw;}finally{Transport.InternalMutation--;}}
  }
  static void Release(Transaction transaction){
   foreach(var source in transaction.Sources){Check(source);Integrations.Block(source.Inventory,false);R.Set(source.Container,"m_inUse",false);heldInventories.Remove(source.Inventory);heldSources.Remove(source.Data.m_uid);}
   lockedPlayerInventory=null;lockedTool=null;
  }
  static void Cleanup(Transaction transaction){try{Release(transaction);Actions.Active=null;current=null;blockedPlayer=null;}catch(Exception error){Plugin.Debug(transaction.Pending.Op.Id+" cleanup pending: "+error.Message);}}
  internal static void Tick(){if(current==null)return;if(current.Compensating){TryCompensate(current);return;}if(current.Cleaning)Cleanup(current);}
  internal static void ClearWorld(){if(current!=null){foreach(var source in current.Sources){Integrations.Block(source.Inventory,false);if(source.Container)R.Set(source.Container,"m_inUse",false);}heldInventories.Clear();heldSources.Clear();lockedPlayerInventory=null;lockedTool=null;current=null;blockedPlayer=null;Actions.Active=null;}}
  static void Check(Source source){if(source.Data==null||!source.Data.IsValid()||source.Data.GetOwner()!=source.Owner||source.Data.DataRevision!=source.Revision)throw new InvalidOperationException("source fence changed");var bytes=InventoryBytes(source.Inventory);if(!bytes.SequenceEqual(source.Expected))throw new InvalidOperationException("source contents changed");}
  static void CheckPlayer(Transaction transaction){var expected=transaction.PlayerDelta?.Applied==true?transaction.PlayerExpected:transaction.PlayerBefore;if(expected!=null&&!InventoryBytes(transaction.Player.GetInventory()).SequenceEqual(expected))throw new InvalidOperationException("player contents changed");}
  static void Save(Source source){CheckOwner(source);R.Call(source.Container,"Save");source.Expected=InventoryBytes(source.Inventory);source.Revision=source.Data.DataRevision;}
  static void CheckOwner(Source source){if(source.Data==null||!source.Data.IsValid()||source.Data.GetOwner()!=source.Owner||source.Data.DataRevision!=source.Revision)throw new InvalidOperationException("source fence changed before save");}
  static byte[] InventoryBytes(Inventory inventory){var package=new ZPackage();inventory.Save(package);return package.GetArray();}
 }
}
 #endif
