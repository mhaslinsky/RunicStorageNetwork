using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RunicStorageNetwork {
 internal static partial class StorageIndex {
  // Allocated only after all optional game patches have been installed.
  // The ordinary index keeps its original update/reconciliation routines.
  internal sealed class UnloadedMode {
   internal readonly Func<bool> Enabled;
   readonly Func<bool> demand;
   readonly Func<Container,bool> ready;
   readonly Func<Container,Exception,bool> readFailure;
   readonly Queue<Entry> pending=new Queue<Entry>();
   readonly HashSet<Entry> queued=new HashSet<Entry>();
   internal UnloadedMode(Func<bool> enabled,Func<bool> demand,Func<Container,bool> ready,Func<Container,Exception,bool> readFailure){Enabled=enabled;this.demand=demand;this.ready=ready;this.readFailure=readFailure;}
   internal void Clear(){pending.Clear();queued.Clear();}
   internal void Suspend(){Clear();foreach(var e in order){e.Audit=0;e.Dirty=true;}}
   internal void Schedule(Entry e){if(queued.Add(e))pending.Enqueue(e);}
   internal void Tick(){
    if(!demand()||pending.Count==0)return;
    var clock=Stopwatch.StartNew();int budget=Math.Min(16,pending.Count);
    while(pending.Count>0&&budget-->0){var e=pending.Dequeue();queued.Remove(e);
     if(!entries.TryGetValue(e.Key,out var current)||current!=e)continue;
     if(!e.Container||!R.Valid(R.View(e.Container))){Remove(e);continue;}
     Update(e);if(clock.Elapsed.TotalMilliseconds>=.75)break;
    }
   }
   internal void Update(Entry e,bool force=false){
    var c=e.Container;var z=R.View(c).GetZDO();
    if(e.Owner!=z.GetOwner()){e.Owner=z.GetOwner();e.Epoch=++epoch;e.Dirty=true;}
    if(!force&&e.Loaded&&!e.Dirty&&e.DataRevision==z.DataRevision)return;
    if(!ready(c)){Schedule(e);return;}
    e.Reading=true;
    try{
     if(!Transport.Locked(e.Inventory)&&!Integrations.IsBusy(e.Inventory)&&!c.IsInUse()&&z.GetInt(ZDOVars.s_inUse)==0)R.Call(c,"Load");
     var stock=Stockroom.Preview(c);e.DataRevision=z.DataRevision;e.Dirty=false;e.Loaded=true;
     e.Audit=float.PositiveInfinity;catalog.Replace(e.Key,e.Epoch,++e.Revision,stock);
    }catch(Exception error) when(readFailure(c,error)){
     catalog.Remove(e.Key);e.Loaded=false;e.Dirty=true;
    }finally{e.Reading=false;}
   }
   internal void Reconcile(Core core){
    if(!core)return;foreach(var c in core.Pool){Register(c);if(c&&entries.TryGetValue(R.Key(R.View(c).GetZDO().m_uid),out var e))Schedule(e);}
   }
  }
 }
}
