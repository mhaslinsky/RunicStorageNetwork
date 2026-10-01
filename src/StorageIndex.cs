using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static partial class StorageIndex {
  internal static UnloadedMode Offline;
  static bool UseOffline=>Offline!=null&&Offline.Enabled();
  internal sealed class Entry {
   internal string Key;internal Container Container;internal Inventory Inventory;internal Action Changed;
   internal long Owner,Epoch,Revision;internal uint DataRevision;internal bool Dirty=true,Loaded,Reading;internal float Audit;
  }
  static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
  static readonly List<Entry> order=new List<Entry>();
  static void Schedule(Entry e){if(UseOffline)Offline.Schedule(e);}
  internal static void Changed(string key){if(entries.TryGetValue(key,out var e)){e.Dirty=true;Schedule(e);}}
  static readonly ResourceCatalog catalog=new ResourceCatalog();
  static int cursor;static long epoch;
  internal static int Revision=>catalog.Revision;
  internal static long Stamp(IEnumerable<Need> needs)=>catalog.Stamp(needs.Select(n=>n.Item));
  internal static void Clear(){foreach(var e in order)if(e.Inventory!=null)e.Inventory.m_onChanged-=e.Changed;entries.Clear();order.Clear();Offline?.Clear();catalog.Clear();cursor=0;}
  internal static void Register(Container c){
   var v=R.View(c);if(!c||!R.Valid(v)||c.GetInventory()==null)return;
   string key=R.Key(v.GetZDO().m_uid);
   if(entries.TryGetValue(key,out var old)){
    if(old.Container==c&&ReferenceEquals(old.Inventory,c.GetInventory()))return;
    Remove(old);
   }
   var e=new Entry{Key=key,Container=c,Inventory=c.GetInventory(),Owner=v.GetZDO().GetOwner(),Epoch=++epoch};
   e.Changed=()=>{if(!e.Reading){e.Dirty=true;Schedule(e);}};e.Inventory.m_onChanged+=e.Changed;
   entries[key]=e;order.Add(e);Schedule(e);
  }
  static void Remove(Entry e){e.Inventory.m_onChanged-=e.Changed;entries.Remove(e.Key);order.Remove(e);catalog.Remove(e.Key);}
  internal static void Tick(){
   if(UseOffline){Offline.Tick();return;}
   var clock=Stopwatch.StartNew();int budget=Math.Min(16,order.Count);
   while(budget-->0&&order.Count>0){
    if(cursor>=order.Count)cursor=0;var e=order[cursor++];
    if(!e.Container||!R.Valid(R.View(e.Container))){Remove(e);continue;}
    Update(e);
    if(clock.Elapsed.TotalMilliseconds>=.75)break;
   }
  }
  static void Update(Entry e,bool force=false){
   var c=e.Container;var view=R.View(c);var z=view.GetZDO();float now=Time.unscaledTime;
   if(e.Owner!=z.GetOwner()){e.Owner=z.GetOwner();e.Epoch=++epoch;e.Dirty=true;}
   // Revision checks are cheap; unchanged inventories are not deserialized or counted.
   if(!force&&e.Loaded&&!e.Dirty&&e.DataRevision==z.DataRevision&&now<e.Audit)return;
   if(!ZNetScene.instance.IsAreaReady(c.transform.position))return;
   e.Reading=true;
   try{
    if(!Transport.Locked(e.Inventory)&&!Integrations.IsBusy(e.Inventory)&&!c.IsInUse()&&z.GetInt(ZDOVars.s_inUse)==0)R.Call(c,"Load");
    var stock=Stockroom.Preview(c);e.DataRevision=z.DataRevision;e.Dirty=false;e.Loaded=true;
    // Bounded reconciliation for mods that change stacks without Inventory.Changed.
    e.Audit=now+30;catalog.Replace(e.Key,e.Epoch,++e.Revision,stock);
   }finally{e.Reading=false;}
  }
  internal static bool Ready(Core core)=>core&&core.Pool.All(c=>c&&entries.TryGetValue(R.Key(R.View(c).GetZDO().m_uid),out var e)&&e.Loaded);
  internal static List<Stock> Query(Core core,long player,IEnumerable<Need> needs,Vector3? point=null){
   var result=new List<Stock>();if(!core)return result;
   foreach(var group in catalog.Find(needs.Select(n=>n.Item)).GroupBy(s=>s.Source))
    if(entries.TryGetValue(group.Key,out var e)&&Access.Container(e.Container,player,core,out _,ownLease:true))result.AddRange(GatewayRuntime.Local(core,player,e.Container,group,point));
   return result;
  }
  internal static List<Stock> Browse(Core core,long player,Vector3? point=null){
   var result=new List<Stock>();if(!core)return result;
   foreach(var c in core.Pool){
    if(!c||!R.Valid(R.View(c))||!Access.Container(c,player,core,out _,ownLease:true))continue;
    result.AddRange(GatewayRuntime.Local(core,player,c,catalog.Source(R.Key(R.View(c).GetZDO().m_uid)),point));
   }
   return result;
  }
  internal static IEnumerable<Container> Candidates(Core core,long player,IEnumerable<Need> needs,bool discover){
   var names=new HashSet<string>(catalog.Find(needs.Select(n=>n.Item)).Select(s=>s.Source),StringComparer.Ordinal);
   foreach(var c in core.Pool){
    if(!c||!R.Valid(R.View(c)))continue;string key=R.Key(R.View(c).GetZDO().m_uid);
    if(names.Contains(key)||discover||!entries.TryGetValue(key,out var e)||!e.Loaded||e.Dirty||e.DataRevision!=R.View(c).GetZDO().DataRevision)
     if(Access.Container(c,player,core,out _,ownLease:true))yield return c;
   }
  }
  internal static void Fresh(string key){
   if(!entries.TryGetValue(key,out var e)||!e.Container||!R.Valid(R.View(e.Container)))return;
   if(UseOffline)Offline.Update(e,true);else Update(e,true);
  }
  internal static void Reconcile(Core core){
   if(UseOffline){Offline.Reconcile(core);return;}
   if(!core)return;foreach(var c in core.Pool){Register(c);if(c&&entries.TryGetValue(R.Key(R.View(c).GetZDO().m_uid),out var e))e.Dirty=true;}
  }
 }
}
