using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorageNetwork {
 internal enum FastPathBuildEntry { Continue, Native, Refuse }
 internal sealed class FastPathFenceException:InvalidOperationException {
  internal FastPathFenceException(string message):base(message){}
 }
 internal sealed class FastPathPermanentRestoreException:InvalidOperationException {
  internal FastPathPermanentRestoreException(string message,Exception inner=null):base(message,inner){}
 }
 internal sealed class FastPathSourceState {
  internal bool Valid,SceneOwner,Replica,Access,InUse,NetworkInUse,Locked,Reserved,Busy,Held;
  internal long Owner,LocalOwner;
 }
 internal abstract class FastPathCoreSource {
  internal abstract string Key {get;}
  internal abstract long Owner {get;}
  internal abstract long CurrentOwner {get;}
  internal abstract uint Revision {get;}
  internal abstract bool IsValid {get;}
  internal abstract byte[] Bytes {get;}
  internal abstract string DebitDescription {get;}
  internal abstract bool RestoreComplete {get;}
  internal abstract void AcquireHold();
  internal abstract void ApplyDebit();
  internal abstract void Save();
  internal abstract void RestoreDebit();
  internal abstract void ReleaseHold();
  internal abstract void ClearHold();
 }
 internal abstract class FastPathCorePlayer {
  internal abstract byte[] Bytes {get;}
  internal abstract string DebitDescription {get;}
  internal abstract bool RestoreComplete {get;}
  internal abstract void AcquireHold();
  internal abstract void ApplyDebit();
  internal abstract void RestoreDebit();
  internal abstract void ReleaseHold();
  internal abstract void ClearHold();
 }
 internal sealed class FastPathCore {
  sealed class HeldSource {
   internal FastPathCoreSource Source;
   internal long ExpectedOwner;
   internal uint ExpectedRevision;
   internal byte[] Before,ExpectedBytes;
   internal bool Applied,Restored,Released;
  }
  sealed class Transaction {
   internal FastPathCorePlayer Player;
   internal List<HeldSource> Sources;
   internal byte[] PlayerBefore,PlayerExpected;
   internal bool PlayerApplied,PlayerHeld,Compensating,Cleaning,Output,Charged;
   internal Action Finish;
   internal Action<bool> PlacementChanged;
  }
  readonly Action<string> warning,debug;
  Transaction current;
  bool executing,nativeCall;

  internal FastPathCore(Action<string> warning,Action<string> debug=null){this.warning=warning??throw new ArgumentNullException(nameof(warning));this.debug=debug??(_=>{});}
  internal bool Running=>current!=null;
  internal bool Active=>executing;
  internal bool Compensating=>current!=null&&current.Compensating;
  internal bool Cleaning=>current!=null&&current.Cleaning;
  internal bool Blocks(FastPathCorePlayer player)=>current!=null&&current.Player==player&&!executing;
  internal bool NativeReady(FastPathCorePlayer player)=>current!=null&&current.Player==player&&nativeCall;
  internal bool ConsumeNativePlacement(FastPathCorePlayer player){if(!NativeReady(player))return false;nativeCall=false;return true;}
  internal static FastPathBuildEntry BuildEntry(bool blocked,bool running,bool owns,bool native){
   if(!owns)return FastPathBuildEntry.Continue;
   if(blocked)return FastPathBuildEntry.Refuse;
   if(running)return native?FastPathBuildEntry.Native:FastPathBuildEntry.Refuse;
   return FastPathBuildEntry.Continue;
  }
  internal static bool AllowOriginalBuild(bool contentAllowed,bool entryAllowed)=>contentAllowed&&entryAllowed;
  internal static bool SourceEligible(FastPathSourceState source)=>source!=null&&source.Valid&&source.SceneOwner&&source.Owner==source.LocalOwner&&source.Access&&!source.Replica&&!source.InUse&&!source.NetworkInUse&&!source.Locked&&!source.Reserved&&!source.Busy&&!source.Held;
  internal static bool PlayerEligible(bool enabled,bool local,bool carried,bool inventoryLocked,bool toolLocked,bool waiting,bool active,bool running)=>enabled&&local&&!carried&&!inventoryLocked&&!toolLocked&&!waiting&&!active&&!running;
  internal static bool PlanSourcesMatch(IEnumerable<string> selected,IEnumerable<string> actual){
   if(selected==null||actual==null)return false;
   var selectedSet=new HashSet<string>(selected,StringComparer.Ordinal);var actualSet=new HashSet<string>(actual,StringComparer.Ordinal);
   return selectedSet.Count>0&&selectedSet.All(key=>!string.IsNullOrEmpty(key)&&key!="player")&&selectedSet.SetEquals(actualSet);
  }
  internal static List<T> RestoreOrder<T>(IEnumerable<T> items,IReadOnlyList<int> before,Func<T,int> slot){
   var ordered=items.ToList();var positions=new Dictionary<int,int>();
   for(int index=0;index<before.Count;index++)positions.Add(before[index],index);
   if(ordered.Count!=before.Count||!new HashSet<int>(ordered.Select(slot)).SetEquals(before))throw new FastPathPermanentRestoreException("restored inventory slots changed");
   return ordered.OrderBy(item=>positions[slot(item)]).ToList();
  }

  internal bool TryBuild(FastPathCorePlayer player,IReadOnlyList<FastPathCoreSource> sources,Func<bool> place,Action finish,Func<bool> outputObserved=null,Action<bool> placementChanged=null){
   if(player==null||sources==null||sources.Count==0||sources.Any(source=>source==null)||place==null||finish==null||current!=null)return false;
   var transaction=new Transaction{Player=player,Sources=new List<HeldSource>(),PlayerBefore=Copy(player.Bytes),Finish=finish,PlacementChanged=placementChanged};
   transaction.PlayerExpected=Copy(transaction.PlayerBefore);current=transaction;bool wrote=false;
   try {
    transaction.PlayerHeld=true;player.AcquireHold();
    foreach(var source in sources){
     var before=Copy(source.Bytes);var held=new HeldSource{Source=source,ExpectedOwner=source.Owner,ExpectedRevision=source.Revision,Before=before,ExpectedBytes=Copy(before)};
     transaction.Sources.Add(held);source.AcquireHold();
    }
    foreach(var held in transaction.Sources){Check(held);held.Applied=true;wrote=true;Mutate(held,false);}
    CheckPlayer(transaction);transaction.PlayerApplied=true;wrote=true;
    try{player.ApplyDebit();}finally{transaction.PlayerExpected=Copy(player.Bytes);}
    executing=true;nativeCall=true;
    try{transaction.PlacementChanged?.Invoke(true);transaction.Output=place();}
    finally{try{transaction.Output|=outputObserved?.Invoke()??false;}finally{nativeCall=false;executing=false;transaction.PlacementChanged?.Invoke(false);}}
    if(!transaction.Output)throw new InvalidOperationException("placement refused");
    Charge(transaction);transaction.Cleaning=true;Cleanup(transaction);
   }catch(Exception error){
    debug("fast path transaction: "+error.Message);
    if(transaction.Output){
     try{Charge(transaction);}catch(Exception charge){warning("fast path build cost incomplete: "+charge.Message);}
     transaction.Cleaning=true;Cleanup(transaction);
    }else{transaction.Compensating=true;Compensate(transaction);}
   }
   return wrote||current!=null;
  }

  static byte[] Copy(byte[] bytes){if(bytes==null)throw new InvalidOperationException("inventory bytes missing");return (byte[])bytes.Clone();}
  void Charge(Transaction transaction){if(transaction.Charged)return;transaction.Charged=true;try{transaction.Finish();}catch(Exception error){warning("fast path build cost incomplete: "+error.Message);throw;}}
  bool FenceMatches(HeldSource held)=>held.Source.IsValid&&held.Source.CurrentOwner==held.ExpectedOwner&&held.Source.Revision==held.ExpectedRevision&&held.Source.Bytes.SequenceEqual(held.ExpectedBytes);
  void Check(HeldSource held){if(!FenceMatches(held))throw new FastPathFenceException("source fence changed: "+held.Source.Key);}
  void CheckOwner(HeldSource held){if(!held.Source.IsValid||held.Source.CurrentOwner!=held.ExpectedOwner)throw new FastPathFenceException("source owner changed: "+held.Source.Key);}
  void Recapture(HeldSource held){held.ExpectedBytes=Copy(held.Source.Bytes);held.ExpectedRevision=held.Source.Revision;}
  void CaptureAfterMutation(HeldSource held){if(held.Source.IsValid&&held.Source.CurrentOwner==held.ExpectedOwner)Recapture(held);}
  void Mutate(HeldSource held,bool restore){
   try {
    // A completed delta cannot repair a changed inventory on later frames.
    if(restore){
     if(!held.Restored){held.Source.RestoreDebit();held.Restored=held.Source.RestoreComplete;if(!held.Restored)throw new InvalidOperationException("source restore incomplete");}
     if(!held.Source.Bytes.SequenceEqual(held.Before))throw new FastPathPermanentRestoreException("restored inventory bytes changed");
    }
    else held.Source.ApplyDebit();
    // Vanilla change-save advances the revision inside the mutation.
    CheckOwner(held);held.Source.Save();Recapture(held);
    if(restore){if(!held.ExpectedBytes.SequenceEqual(held.Before))throw new FastPathPermanentRestoreException("restored inventory bytes changed");held.Applied=false;}
   }catch(Exception error){CaptureAfterMutation(held);if(restore&&held.Source.RestoreComplete&&!held.Source.Bytes.SequenceEqual(held.Before)&&!(error is FastPathPermanentRestoreException))throw new FastPathPermanentRestoreException(error.Message,error);throw;}
  }
  void CheckPlayer(Transaction transaction){if(!transaction.Player.Bytes.SequenceEqual(transaction.PlayerExpected))throw new FastPathFenceException("player contents changed");}
  void WarnDebit(string key,string description){if(!string.IsNullOrEmpty(description))warning("fast path debit unrestored key="+key+" items="+description);}
  void Compensate(Transaction transaction){
   if(transaction.PlayerApplied){
    string description=transaction.Player.DebitDescription;
    try {
     if(!transaction.Player.Bytes.SequenceEqual(transaction.PlayerExpected)){WarnDebit("player",description);transaction.PlayerApplied=false;}
     else {
      try{transaction.Player.RestoreDebit();}finally{transaction.PlayerExpected=Copy(transaction.Player.Bytes);}
      if(!transaction.Player.RestoreComplete)throw new InvalidOperationException("player restore incomplete");
      if(!transaction.PlayerExpected.SequenceEqual(transaction.PlayerBefore))throw new FastPathPermanentRestoreException("restored player bytes changed");
      transaction.PlayerApplied=false;
     }
    }catch(Exception error){
     if(error is FastPathPermanentRestoreException||(transaction.Player.RestoreComplete&&!transaction.PlayerExpected.SequenceEqual(transaction.PlayerBefore))){WarnDebit("player",description);transaction.PlayerApplied=false;}
     else debug("fast path player compensation pending: "+error.Message);
    }
   }
   if(!transaction.PlayerApplied&&transaction.PlayerHeld){try{ReleasePlayer(transaction);}catch(Exception error){debug("fast path player release pending: "+error.Message);}}
   for(int index=transaction.Sources.Count-1;index>=0;index--){var held=transaction.Sources[index];if(!held.Applied)continue;
    string description=held.Source.DebitDescription;
    try {
     if(!FenceMatches(held)){WarnDebit(held.Source.Key,description);held.Applied=false;}
     else Mutate(held,true);
    }catch(FastPathPermanentRestoreException){WarnDebit(held.Source.Key,description);held.Applied=false;}
    catch(Exception error){debug("fast path compensation pending key="+held.Source.Key+": "+error.Message);}
    if(!held.Applied){try{Release(held);}catch(Exception error){debug("fast path release pending key="+held.Source.Key+": "+error.Message);}}
   }
   if(transaction.PlayerApplied||transaction.Sources.Any(held=>held.Applied))return;
   transaction.Compensating=false;transaction.Cleaning=true;Cleanup(transaction);
  }
  void Release(HeldSource held){if(held.Released)return;held.Source.ReleaseHold();held.Released=true;}
  void ReleasePlayer(Transaction transaction){if(!transaction.PlayerHeld)return;transaction.Player.ReleaseHold();transaction.PlayerHeld=false;}
  void Cleanup(Transaction transaction){
   foreach(var held in transaction.Sources.Where(held=>!held.Released)){
    try{Release(held);}catch(Exception error){debug("fast path release pending key="+held.Source.Key+": "+error.Message);}
   }
   if(transaction.Sources.Any(held=>!held.Released))return;
   try{ReleasePlayer(transaction);}catch(Exception error){debug("fast path player release pending: "+error.Message);return;}
   current=null;
  }
  internal void Tick(){if(current==null)return;if(current.Compensating)Compensate(current);else if(current.Cleaning)Cleanup(current);}
  internal void Clear(){
   var transaction=current;if(transaction==null)return;
   try {
    if(!transaction.Output){
     foreach(var held in transaction.Sources.Where(held=>held.Applied))WarnDebit(held.Source.Key,held.Source.DebitDescription);
     if(transaction.PlayerApplied)WarnDebit("player",transaction.Player.DebitDescription);
    }
    foreach(var held in transaction.Sources.Where(held=>!held.Released)){
     try{held.Source.ClearHold();}catch(Exception error){warning("fast path world hold cleanup failed key="+held.Source.Key+": "+error.Message);}finally{held.Released=true;}
    }
    try{transaction.Player.ClearHold();}catch(Exception error){warning("fast path world player cleanup failed: "+error.Message);}
   }finally{nativeCall=false;executing=false;current=null;transaction.PlacementChanged?.Invoke(false);}
  }
 }
}
