using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static class TerminalTransfer {
  internal const string Prefix="rsn_take:";
  internal const float UseDistance=5f;
  internal const int MaxBytes=1024*1024;
  sealed class Pending {internal Operation Op;internal Player Player;internal Core Core;internal StorageCodex AccessPoint;internal float Sent,Started;internal bool Cancelled;internal TerminalDelivery Delivery;internal string Failure;}
  static Pending pending;
  static readonly OutcomeReceipts receipts=new OutcomeReceipts();
  internal static bool Busy=>pending!=null;
  internal static bool Contains(string id,string key)=>pending?.Op.Id==id&&pending.Op.Sources.Contains(key);
  internal static void Clear(){pending=null;receipts.Clear();}
  internal static void Cancel(){if(pending!=null)pending.Cancelled=true;}
  internal static bool CanUse(StorageCodex access,Core core,Player p)=>ContentSettings.TerminalEnabled&&Plugin.Enabled&&access&&access.Valid&&core&&core.Valid&&p&&p==Player.m_localPlayer&&!p.IsDead()&&
   Vector3.Distance(access.transform.position,p.transform.position)<=UseDistance&&Access.Ward(access.transform.position,p.GetPlayerID())&&Access.Ward(core.transform.position,p.GetPlayerID())&&Topology.Supplies(core,access.transform.position,p.GetPlayerID());
  internal static bool Requirements(Operation op,out string reason){
   reason="invalid terminal request";
   if(!op.Withdrawal||op.Build||op.Quote||op.PlayerStock.Count!=0||op.Multiplier<1||op.Multiplier>TerminalRules.MaxAmount||op.Quality<1||op.Quality>100)return false;
   string name=op.Target.Substring(Prefix.Length);var prefab=ZNetScene.instance.GetPrefab(name);var item=prefab?prefab.GetComponent<ItemDrop>():null;
   if(!item||name.Length==0||name.Length>140)return false;
   op.Needs=new List<Need>{new Need(name,op.Multiplier,op.Quality)};reason="ok";return true;
  }
  internal static bool Validate(Operation op,out Player p,out Core core,out string reason){
   p=Player.m_localPlayer;core=Operation.CoreObject(op.Core);reason="terminal unavailable";
   if(!CanUse(StorageCodex.Find(op.Station),core,p)||p.GetZDOID()!=op.Actor||p.GetPlayerID()!=op.PlayerId||op.Peer!=ZNet.GetUID())return false;
   return new RemoteContext(op).Validate(out reason);
  }
  internal static bool Start(StorageCodex access,Core core,Player p,string item,int quality,int amount){
   if(Busy||Actions.Waiting!=null||CraftPreparation.HasReservation||!CanUse(access,core,p))return false;
   // Station carries the access-point identity for withdrawals; Core still identifies storage.
   var op=new Operation{Id=Guid.NewGuid().ToString("N"),Target=Prefix+item,Quality=quality,Multiplier=amount,Actor=p.GetZDOID(),Station=access.Id,Core=core.Id,Network=core.GetComponent<NetworkMember>().SavedNetwork,Peer=ZNet.GetUID(),PlayerId=p.GetPlayerID()};
   if(!op.Validate(out _,out _,out _))return false;
   var plan=Planner.Plan(op.Needs,StorageIndex.Query(core,p.GetPlayerID(),op.Needs,access.transform.position),true);
   if(plan==null){StorageIndex.Reconcile(core);return false;}
   var proposal=new Actions.Pending{Op=op,Player=p};if(!Actions.Propose(proposal,plan))return false;
   op.PlayerStock.Clear(); // Withdrawals never pay from the character's own inventory.
   pending=new Pending{Op=op,Player=p,Core=core,AccessPoint=access,Started=Time.unscaledTime,Sent=-100};Tick();return true;
  }
  internal static void Tick(){
   var current=pending;if(current==null)return;
   if(!CanUse(current.AccessPoint,current.Core,current.Player)||current.AccessPoint.Id!=current.Op.Station)current.Cancelled=true;
   if(Time.unscaledTime-current.Sent<2)return;current.Sent=Time.unscaledTime;
   if(Time.unscaledTime-current.Started>10)Plugin.Critical(current.Op.Id,"Terminal transfer acknowledgement delayed; retrying the same request");
   try{Transport.Instance.Begin(current.Op);}catch(Exception e){Plugin.Debug("terminal retry: "+e.Message);}
  }
  internal static byte[] Pack(InventoryDelta delta){
   if(delta.Parts.Count>TerminalRules.MaxParcels)throw new InvalidOperationException("terminal parcel limit");
   var inventory=new Inventory("RSN transfer",null,8,32);int index=0;
   foreach(var part in delta.Parts){var item=part.Item.Clone();item.m_stack=part.Amount;item.m_equipped=false;item.m_gridPos=new Vector2i(index%8,index/8);inventory.GetAllItems().Add(item);index++;}
   var p=new ZPackage();inventory.Save(p);var bytes=p.GetArray();if(bytes.Length>MaxBytes)throw new InvalidOperationException("terminal parcel too large");return bytes;
  }
  static List<ItemDrop.ItemData> Unpack(ZPackage p,List<Debit> plan){
   int count=p.ReadInt();if(count<1||count>TerminalRules.MaxParcels)throw new InvalidOperationException("terminal source count");
   var sources=new HashSet<string>();var items=new List<ItemDrop.ItemData>();var stock=new List<Stock>();int bytes=0;
   for(int i=0;i<count;i++){
    string key=p.ReadString();var data=p.ReadByteArray();bytes+=data.Length;
    if(!sources.Add(key)||!plan.Any(d=>d.Source==key)||bytes>MaxBytes)throw new InvalidOperationException("invalid terminal parcel");
    var inventory=new Inventory("RSN transfer",null,8,32);inventory.Load(new ZPackage(data));
    foreach(var item in inventory.GetAllItems()){
     if(!item.m_dropPrefab||item.m_stack<1||item.m_stack>item.m_shared.m_maxStackSize)throw new InvalidOperationException("invalid terminal item");
     items.Add(item);stock.Add(new Stock(key,item.m_dropPrefab.name,item.m_quality,item.m_stack));
    }
    if(items.Count>TerminalRules.MaxParcels)throw new InvalidOperationException("terminal parcel limit");
   }
   if(!TerminalRules.Matches(stock,plan))throw new InvalidOperationException("terminal parcel differs from payment");return items;
  }
  internal static void Ready(long sender,ZPackage p){
   if(sender!=Transport.Server)return;string id=p.ReadString();
   if(receipts.TryGet(id,out var old)){Transport.Instance.Result(id,old.Success,old.Reason);return;}
   var current=pending;
   if(current==null||current.Op.Id!=id){Record(id,false,"terminal closed");return;}
   bool success=false;string reason=current.Failure??"terminal unavailable";
   Transport.InternalMutation++;
   try{
    if(current.Failure!=null){current.Delivery?.Restore();}
    else{
     if(current.Cancelled||!NetworkTerminal.Showing(current.AccessPoint,current.Core)||!current.Op.Validate(out _,out _,out reason))throw new InvalidOperationException(reason);
     var plan=Wire.Debits(p);
     if(plan.Count==0||plan.Any(d=>!current.Op.Sources.Contains(d.Source)||d.Item!=current.Op.Needs[0].Item||d.Quality!=current.Op.Quality)||plan.Sum(d=>(long)d.Amount)!=current.Op.Multiplier)throw new InvalidOperationException("invalid terminal plan");
     if(!GatewayRuntime.ValidatePlan(current.Op,plan))throw new InvalidOperationException("gateway path changed before withdrawal");
     var items=Unpack(p,plan);current.Delivery=new TerminalDelivery(current.Player.GetInventory());current.Delivery.Apply(items);success=true;
    }
   }catch(Exception e){
    reason=e.Message;current.Failure=reason;Plugin.Debug(id+" terminal delivery: "+reason);
    try{current.Delivery?.Restore();}catch(Exception rollback){Plugin.Error(id+" terminal rollback",rollback);return;}
   }finally{Transport.InternalMutation--;}
   receipts.Record(id,success,success?"terminal items delivered":reason);
   // A refused transaction stays pending until the owners acknowledge rollback.
   if(success){pending=null;StorageIndex.Reconcile(current.Core);NetworkTerminal.TransferStatus("terminal_received",current.Op.Multiplier);}
   else NetworkTerminal.TransferStatus(reason=="terminal inventory full"?"terminal_full":"terminal_retry");
   Transport.Instance.Result(id,success,success?"terminal items delivered":reason);
  }
  static void Record(string id,bool success,string reason){receipts.Record(id,success,reason);Transport.Instance.Result(id,success,reason);}
  internal static void Refused(string id,string reason){
   if(pending?.Op.Id!=id)return;var core=pending.Core;pending=null;StorageIndex.Reconcile(core);Plugin.Debug(id+" terminal refused: "+reason);NetworkTerminal.TransferStatus(reason.Contains("inventory full")?"terminal_full":"terminal_retry");
  }
 }
}
