using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorageNetwork {
 internal enum FastPathBuildEntry { Continue, Native, Refuse }
 internal sealed class FastPathFenceException:InvalidOperationException {
  internal FastPathFenceException(string message):base(message){}
 }

 internal abstract class FastPathCoreSource {
  internal abstract string Key {get;}
  internal abstract long Owner {get;}
  internal abstract long CurrentOwner {get;}
  internal abstract uint Revision {get;}
  internal abstract bool IsValid {get;}
  internal abstract byte[] Bytes {get;}
  internal abstract void AcquireHold();
  internal abstract void ApplyDebit();
  internal abstract void Save();
  internal abstract void RestoreDebit();
  internal abstract void ReleaseHold();
  internal abstract string DebitDescription {get;}
 }

 internal abstract class FastPathCorePlayer {
  internal abstract byte[] Bytes {get;}
  internal abstract void AcquireHold();
  internal abstract void ApplyDebit();
  internal abstract void RestoreDebit();
  internal abstract void ReleaseHold();
 }

 internal sealed class FastPathCore {
  sealed class HeldSource {
   internal FastPathCoreSource Source;
   internal long ExpectedOwner;
   internal uint ExpectedRevision;
   internal byte[] ExpectedBytes;
   internal bool Applied;
   internal bool Restored;
   internal bool Released;
  }
  sealed class Transaction {
   internal FastPathCorePlayer Player;
   internal List<HeldSource> Sources;
   internal byte[] PlayerBefore;
   internal byte[] PlayerExpected;
   internal bool PlayerApplied;
   internal bool Compensating;
   internal bool Cleaning;
   internal bool Output;
  }

  readonly Action<string> warning;
  Transaction current;
  FastPathCorePlayer blockedPlayer;
  bool executing;
  bool nativeCall;

  internal FastPathCore(Action<string> warning=null){this.warning=warning??(_=>{});}
  internal bool Running=>current!=null;
  internal bool Active=>executing;
  internal bool Compensating=>current!=null&&current.Compensating;
  internal bool Cleaning=>current!=null&&current.Cleaning;
  internal bool Blocks(FastPathCorePlayer player)=>player!=null&&blockedPlayer==player;
  internal bool NativeReady(FastPathCorePlayer player)=>current!=null&&current.Player==player&&nativeCall;
  internal bool ConsumeNativePlacement(FastPathCorePlayer player){if(!NativeReady(player))return false;nativeCall=false;return true;}
  internal static FastPathBuildEntry BuildEntry(bool blocked,bool running,bool owns,bool native){
   if(!owns)return FastPathBuildEntry.Continue;
   if(blocked)return FastPathBuildEntry.Refuse;
   if(running)return native?FastPathBuildEntry.Native:FastPathBuildEntry.Refuse;
   return FastPathBuildEntry.Continue;
  }
  internal static bool AllowOriginalBuild(bool contentAllowed,bool entryAllowed)=>contentAllowed&&entryAllowed;

  internal bool TryBuild(FastPathCorePlayer player,IReadOnlyList<FastPathCoreSource> sources,Func<bool> place,Action finish,Action reentrant=null){
   if(player==null||sources==null||sources.Count==0||current!=null||blockedPlayer!=null)return false;
   var transaction=new Transaction{Player=player,Sources=new List<HeldSource>(),PlayerBefore=Copy(player.Bytes)};current=transaction;
   try {
    player.AcquireHold();
    foreach(var source in sources){
     if(source==null)throw new FastPathFenceException("source missing");
     var held=new HeldSource{Source=source,ExpectedOwner=source.Owner,ExpectedRevision=source.Revision,ExpectedBytes=Copy(source.Bytes)};
     source.AcquireHold();transaction.Sources.Add(held);
    }
    foreach(var held in transaction.Sources)Apply(held);
    ApplyPlayer(transaction);
    reentrant?.Invoke();
    bool placed;executing=true;nativeCall=true;try{placed=place();}finally{nativeCall=false;executing=false;}
    if(!placed)throw new InvalidOperationException("placement refused");
    transaction.Output=true;
    finish();
    if(Release(transaction)){current=null;return true;}
    transaction.Cleaning=true;return true;
   }catch(Exception error){
    bool wrote=transaction.PlayerApplied||transaction.Sources.Any(held=>held.Applied);
    if(transaction.Output){transaction.Cleaning=true;return true;}
    blockedPlayer=player;transaction.Compensating=true;TryCompensate(transaction,error);
    return wrote||current!=null;
   }
  }

  void Apply(HeldSource held){
   Check(held);
   held.Applied=true;
   try {
    held.Source.ApplyDebit();
    held.Source.Save();
    Recapture(held);
   }catch {
    CaptureAfterMutation(held);
    throw;
   }
  }

  void ApplyPlayer(Transaction transaction){
   try {transaction.Player.ApplyDebit();transaction.PlayerApplied=true;transaction.PlayerExpected=Copy(transaction.Player.Bytes);}
   catch {transaction.PlayerExpected=Copy(transaction.Player.Bytes);throw;}
  }

  void TryCompensate(Transaction transaction,Exception error){
   try {Rollback(transaction);if(transaction.Sources.Any(held=>held.Applied)||transaction.PlayerApplied)return;FinishCompensation(transaction);}
   catch(Exception retry){warning("fast path compensation pending: "+retry.Message);}
  }

  void Rollback(Transaction transaction){
   if(transaction.PlayerApplied){
    try {transaction.Player.RestoreDebit();transaction.PlayerExpected=Copy(transaction.Player.Bytes);transaction.PlayerApplied=false;}
    catch {transaction.PlayerExpected=Copy(transaction.Player.Bytes);throw;}
   }
   for(int index=transaction.Sources.Count-1;index>=0;index--){var held=transaction.Sources[index];if(!held.Applied)continue;
    if(!FenceMatches(held)){
     warning("fast path debit unrestored key="+held.Source.Key+" items="+held.Source.DebitDescription);
     held.Applied=false;held.Restored=true;continue;
    }
    try {
     if(!held.Restored){held.Source.RestoreDebit();held.Restored=true;}
     held.Source.Save();Recapture(held);held.Applied=false;
    }catch {CaptureAfterMutation(held);throw;}
   }
  }

  void FinishCompensation(Transaction transaction){
   if(!Release(transaction))return;
   transaction.Compensating=false;blockedPlayer=null;current=null;
  }

  bool Release(Transaction transaction){
   var failed=false;
   foreach(var held in transaction.Sources.Where(held=>!held.Released)){
    try {held.Source.ReleaseHold();held.Released=true;}
    catch(Exception error){failed=true;warning("fast path release pending key="+held.Source.Key+": "+error.Message);}
   }
   if(transaction.Sources.Any(held=>!held.Released))failed=true;
   if(!failed){try{transaction.Player.ReleaseHold();}catch(Exception error){failed=true;warning("fast path player release pending: "+error.Message);}}
   return !failed;
  }

  bool FenceMatches(HeldSource held){
   if(!held.Source.IsValid||held.Source.CurrentOwner!=held.ExpectedOwner||held.Source.Revision!=held.ExpectedRevision)return false;
   return held.Source.Bytes.SequenceEqual(held.ExpectedBytes);
  }
  void Check(HeldSource held){if(!FenceMatches(held))throw new FastPathFenceException("source fence changed: "+held.Source.Key);}
  void Recapture(HeldSource held){held.ExpectedBytes=Copy(held.Source.Bytes);held.ExpectedRevision=held.Source.Revision;held.ExpectedOwner=held.Source.CurrentOwner;}
  void CaptureAfterMutation(HeldSource held){if(held.Source.IsValid&&held.Source.CurrentOwner==held.ExpectedOwner)Recapture(held);}
  static byte[] Copy(byte[] bytes)=>(byte[])(bytes??Array.Empty<byte>()).Clone();

  internal void Tick(){if(current==null)return;if(current.Compensating){TryCompensate(current,new InvalidOperationException("retry"));return;}if(current.Cleaning){if(Release(current)){current=null;blockedPlayer=null;}}}
  internal void Clear(){
   if(current!=null){
    foreach(var held in current.Sources.Where(held=>held.Applied))warning("fast path debit unrestored key="+held.Source.Key+" items="+held.Source.DebitDescription);
    Release(current);current=null;
   }
   nativeCall=false;executing=false;blockedPlayer=null;
  }
 }
}
