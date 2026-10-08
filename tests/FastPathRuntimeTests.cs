#if FAST_PATH_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RunicStorageNetwork;

sealed class RuntimeItem {
 internal string Name;internal int Stack;internal int Quality;internal string Metadata;
 internal RuntimeItem Clone()=>new RuntimeItem{Name=Name,Stack=Stack,Quality=Quality,Metadata=Metadata};
}
sealed class RuntimeInventory {
 internal readonly List<RuntimeItem> Items=new List<RuntimeItem>();
 internal byte[] Bytes()=>Encoding.UTF8.GetBytes(string.Join("|",Items.OrderBy(item=>item.Name).ThenBy(item=>item.Quality).Select(item=>item.Name+":"+item.Stack+":"+item.Quality+":"+item.Metadata)));
 internal void Replace(IEnumerable<RuntimeItem> items){Items.Clear();Items.AddRange(items.Select(item=>item.Clone()));}
}
sealed class RuntimePlayer:FastPathCorePlayer {
 internal readonly RuntimeInventory Inventory=new RuntimeInventory();internal bool BuildAllowed=true;internal bool ToolLocked;internal bool PlacementRefused;internal int Costs;internal int Outputs;internal bool Held;
 internal readonly Dictionary<string,int> Debit;
 internal RuntimePlayer(Dictionary<string,int> debit){Debit=debit;}
 internal override byte[] Bytes=>Inventory.Bytes();
 internal override void AcquireHold(){if(!BuildAllowed||ToolLocked)throw new InvalidOperationException("player ineligible");Held=true;}
 internal override void ApplyDebit(){Remove(Debit);}
 internal override void RestoreDebit(){RestoreSnapshot();}
 internal override void ReleaseHold(){Held=false;}
 readonly List<RuntimeItem> before=new List<RuntimeItem>();
 internal void Snapshot(){before.Clear();before.AddRange(Inventory.Items.Select(item=>item.Clone()));}
 void RestoreSnapshot(){Inventory.Replace(before);}
 void Remove(Dictionary<string,int> debit){foreach(var entry in debit){var left=entry.Value;foreach(var item in Inventory.Items.Where(item=>item.Name==entry.Key).ToArray()){var amount=Math.Min(left,item.Stack);item.Stack-=amount;left-=amount;if(left==0)break;}if(left>0)throw new InvalidOperationException("player stock insufficient");}}
}
sealed class RuntimeChest:FastPathCoreSource {
 internal readonly string Name;internal readonly RuntimeInventory Inventory=new RuntimeInventory();internal readonly Dictionary<string,int> Debit;internal long OwnerId=1;internal uint RevisionId;internal bool Busy;internal bool InUse;internal bool IntegrationBlocked;internal bool FailRemove;internal bool FailRestore;internal bool FailSave;internal bool SaveAfterMutationFailure;internal bool FailRelease;internal bool Valid=true;internal string PersistentLease="";internal int RemoteRefusals;internal bool Restored;internal Action OnChanged;
 readonly List<RuntimeItem> before=new List<RuntimeItem>();internal bool Held;
 internal RuntimeChest(string name,Dictionary<string,int> debit){Name=name;Debit=debit;}
 internal override string Key=>Name;internal override long Owner=>1;internal override long CurrentOwner=>OwnerId;internal override uint Revision=>RevisionId;internal override bool IsValid=>Valid;internal override byte[] Bytes=>Inventory.Bytes();internal override string DebitDescription=>string.Join(",",Debit.Select(entry=>entry.Key+"="+entry.Value));
 internal override void AcquireHold(){if(Busy||InUse||OwnerId!=1||!Valid)throw new FastPathFenceException("ineligible chest "+Name);Snapshot();Held=true;InUse=true;IntegrationBlocked=true;}
 internal override void ApplyDebit(){Remove(Debit);}
 internal override void Save(){if(FailSave&&!SaveAfterMutationFailure)throw new InvalidOperationException("save refused");RevisionId++;OnChanged?.Invoke();if(FailSave)throw new InvalidOperationException("save refused after mutation");}
 internal override void RestoreDebit(){if(Restored)return;if(FailRestore)throw new InvalidOperationException("restore refused");Inventory.Replace(before);Restored=true;Changed();}
 internal override void ReleaseHold(){if(!Held)return;if(FailRelease)throw new InvalidOperationException("release refused");Held=false;InUse=false;IntegrationBlocked=false;}
 void Snapshot(){before.Clear();before.AddRange(Inventory.Items.Select(item=>item.Clone()));}
 void Remove(Dictionary<string,int> debit){foreach(var entry in debit){var left=entry.Value;foreach(var item in Inventory.Items.Where(item=>item.Name==entry.Key).ToArray()){var amount=Math.Min(left,item.Stack);if(amount==0)continue;item.Stack-=amount;left-=amount;Changed();if(FailRemove)throw new InvalidOperationException("RemoveItem refused");if(left==0)break;}if(left>0)throw new InvalidOperationException("chest stock insufficient");}}
 void Changed(){RevisionId++;OnChanged?.Invoke();}
 internal bool RemoteOpen(){if(Held){RemoteRefusals++;return false;}return true;}
 internal bool RemoteStack()=>RemoteOpen();
 internal bool RemoteTakeAll()=>RemoteOpen();
 internal bool CoordinatedPrepare()=>RemoteOpen();
 internal bool OwnershipMove(long owner){return !Held||owner==OwnerId;}
}

static class FastPathRuntimeTests {
 static int passed;static readonly List<string> warnings=new List<string>();
 static void Test(string name,string reason,Action action){action();passed++;Console.WriteLine("PASS fast path "+name+": "+reason);}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static RuntimePlayer Player(Dictionary<string,int> debit){var player=new RuntimePlayer(debit);player.ToolLocked=false;player.Inventory.Items.Add(new RuntimeItem{Name="Wood",Stack=2,Quality=1,Metadata="player"});player.Snapshot();return player;}
 static RuntimeChest Chest(string key,string item,int amount,Dictionary<string,int> debit){var chest=new RuntimeChest(key,debit);chest.Inventory.Items.Add(new RuntimeItem{Name=item,Stack=amount,Quality=1,Metadata=key});return chest;}
 static FastPathCore Engine()=>new FastPathCore(message=>warnings.Add(message));
 static bool Build(FastPathCore engine,RuntimePlayer player,params RuntimeChest[] chests){if(chests.Any(chest=>chest.Busy||chest.OwnerId!=1||!chest.Valid)||player.ToolLocked||!player.BuildAllowed)return false;return engine.TryBuild(player,chests,()=>{if(player.PlacementRefused)return false;player.Outputs++;player.Costs++;return true;},()=>{});}
 static int Main(){
  Test("build entry preserves other players and ordinary flow", "transaction guards apply only to their owning player",()=>{
   Check(FastPathCore.BuildEntry(false,false,false,false)==FastPathBuildEntry.Continue,"ordinary flow was blocked");
   Check(FastPathCore.BuildEntry(false,true,false,false)==FastPathBuildEntry.Continue,"other player was blocked");
   Check(FastPathCore.BuildEntry(false,true,true,false)==FastPathBuildEntry.Refuse,"owning player reentered");
   Check(FastPathCore.BuildEntry(true,true,true,false)==FastPathBuildEntry.Refuse,"compensation allowed a build");
   Check(FastPathCore.BuildEntry(false,true,true,false)==FastPathBuildEntry.Refuse,"cleanup allowed a build");
  });
  Test("nested native build passes once", "the paid placement consumes its one native call before callbacks",()=>{
   var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var engine=Engine();
   bool handled=engine.TryBuild(player,new[]{chest},()=>{
    Check(FastPathCore.BuildEntry(engine.Blocks(player),engine.Running,true,engine.NativeReady(player))==FastPathBuildEntry.Native,"nested call was refused");
    Check(engine.ConsumeNativePlacement(player),"native call was not consumed");
    Check(!engine.ConsumeNativePlacement(player),"native call passed twice");
    Check(FastPathCore.BuildEntry(engine.Blocks(player),engine.Running,true,engine.NativeReady(player))==FastPathBuildEntry.Refuse,"callback build was allowed");
    player.Outputs++;return true;
   },()=>player.Costs++);
   Check(handled&&player.Outputs==1&&player.Costs==1,"paid build did not complete once");
   Check(!FastPathCore.AllowOriginalBuild(true,!handled),"outer prefix allowed another placement");
  });
  Test("mixed player and chest debit", "production core applies the exact mixed plan once",()=>{var player=Player(new Dictionary<string,int>{{"Wood",2}});var first=Chest("first","Wood",3,new Dictionary<string,int>{{"Wood",3}});var second=Chest("second","Wood",4,new Dictionary<string,int>{{"Wood",2}});var engine=Engine();Check(Build(engine,player,first,second),"build was not handled");Check(player.Inventory.Items.Single().Stack==0&&first.Inventory.Items.Single().Stack==0&&second.Inventory.Items.Single().Stack==2,"wrong debit");Check(player.Outputs==1&&player.Costs==1&&!engine.Running&&!engine.Active&&!first.Held&&!second.Held,"cost or release count wrong");});
  Test("vanilla change-save revision fence", "inventory change-save and explicit save recapture the new fence",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var revision=chest.Revision;var engine=Engine();Check(Build(engine,player,chest),"change-save tripped the fence");Check(chest.Revision>revision&&!engine.Running,"transaction did not recapture revision");});
  Test("several debits from one chest", "multiple item groups are removed together",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",5,new Dictionary<string,int>{{"Wood",3},{"Stone",2}});chest.Inventory.Items.Add(new RuntimeItem{Name="Stone",Stack=4,Quality=1,Metadata="one"});var engine=Engine();Check(Build(engine,player,chest),"build was not handled");Check(chest.Inventory.Items.Sum(item=>item.Stack)==4,"mixed debit wrong");});
  Test("partial removal restores bytes", "failed RemoveItem rolls back byte-for-byte",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",5,new Dictionary<string,int>{{"Wood",3}});var before=chest.Bytes;chest.FailRemove=true;chest.FailSave=true;chest.SaveAfterMutationFailure=true;var engine=Engine();Build(engine,player,chest);Check(engine.Running&&engine.Blocks(player),"failed restore should be pending");chest.FailRemove=false;chest.FailSave=false;engine.Tick();Check(chest.Bytes.SequenceEqual(before)&&!engine.Running&&!engine.Blocks(player),"restore was not byte exact");});
  Test("save failure after mutation restores bytes", "a changed revision is recaptured before compensation",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",5,new Dictionary<string,int>{{"Wood",3}});var before=chest.Bytes;chest.FailSave=true;chest.SaveAfterMutationFailure=true;var engine=Engine();Build(engine,player,chest);chest.FailSave=false;engine.Tick();Check(chest.Bytes.SequenceEqual(before)&&!engine.Running,"save failure did not compensate");});
  Test("later source failure restores earlier source", "all applied sources compensate in reverse",()=>{var player=Player(new Dictionary<string,int>());var first=Chest("first","Wood",4,new Dictionary<string,int>{{"Wood",3}});var second=Chest("second","Wood",4,new Dictionary<string,int>{{"Wood",3}});second.FailSave=true;var firstBefore=first.Bytes;var secondBefore=second.Bytes;var engine=Engine();Build(engine,player,first,second);second.FailSave=false;engine.Tick();Check(first.Bytes.SequenceEqual(firstBefore)&&second.Bytes.SequenceEqual(secondBefore),"earlier source was not restored");});
  Test("placement refusal restores payment", "no output leaves player and chest unchanged",()=>{var player=Player(new Dictionary<string,int>());player.PlacementRefused=true;var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var before=chest.Bytes;var engine=Engine();Build(engine,player,chest);player.PlacementRefused=false;engine.Tick();Check(chest.Bytes.SequenceEqual(before)&&player.Inventory.Items.Single().Stack==2&&player.Outputs==0,"placement failure kept a debit");});
  Test("compensation retries without server payment", "a later frame releases the in-memory hold",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRemove=true;chest.FailRestore=true;var engine=Engine();Build(engine,player,chest);Check(engine.Blocks(player)&&chest.Held&&!warnings.Any(message=>message.Contains("unrestored")),"compensation did not hold source");chest.FailRemove=false;chest.FailRestore=false;engine.Tick();Check(!engine.Blocks(player)&&!chest.Held,"compensation did not finish");});
  Test("ownership loss abandons debit", "a permanent fence mismatch releases and warns",()=>{warnings.Clear();var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRemove=true;chest.FailRestore=true;var engine=Engine();Build(engine,player,chest);chest.FailRemove=false;chest.OwnerId=2;engine.Tick();Check(!engine.Running&&!engine.Blocks(player)&&!chest.Held&&chest.Inventory.Items.Single().Stack==1&&warnings.Any(message=>message.Contains("key=one")),"owner loss did not end compensation safely");});
  Test("fence mismatch never writes", "changed bytes are not overwritten during compensation",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRemove=true;chest.FailRestore=true;var engine=Engine();Build(engine,player,chest);chest.FailRemove=false;chest.Inventory.Items.Single().Stack=99;var changed=chest.Bytes;engine.Tick();Check(chest.Bytes.SequenceEqual(changed)&&!engine.Running,"fence mismatch overwrote external stock");});
  Test("world change logs unrestored debit", "clearing compensation leaves no hold and records the approved residual",()=>{warnings.Clear();var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRemove=true;chest.FailRestore=true;var engine=Engine();Build(engine,player,chest);engine.Clear();Check(!engine.Running&&!engine.Blocks(player)&&!chest.Held&&warnings.Any(message=>message.Contains("key=one")),"world clear hid or retained the debit");});
  Test("remote operations refuse a held chest", "open, stack, take-all, prepare and ownership transfer see the in-memory hold",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRemove=true;chest.FailRestore=true;var engine=Engine();Build(engine,player,chest);Check(!chest.RemoteOpen()&&!chest.RemoteStack()&&!chest.RemoteTakeAll()&&!chest.CoordinatedPrepare()&&!chest.OwnershipMove(2),"remote operation was allowed");chest.FailRemove=false;chest.FailRestore=false;engine.Tick();});
  Test("reentrant build is refused", "the active production transaction rejects a callback",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var engine=Engine();bool callbackResult=true;chest.OnChanged=()=>callbackResult=engine.TryBuild(player,new[]{chest},()=>true,()=>{});Build(engine,player,chest);Check(!callbackResult&&player.Outputs==1,"reentrant build was accepted");});
  Test("post-output release retries", "payment stays while cleanup releases",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.FailRelease=true;var engine=Engine();Build(engine,player,chest);Check(player.Outputs==1&&engine.Running&&chest.Held&&!engine.Blocks(player),"output did not enter cleanup");chest.Inventory.Items.Add(new RuntimeItem{Name="Iron",Stack=1,Quality=3,Metadata="changed"});chest.FailRelease=false;engine.Tick();Check(!engine.Running&&!chest.Held,"cleanup did not release changed chest");});
  Test("active clears on every return", "active state is only set around placement",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var engine=Engine();Build(engine,player,chest);Check(!engine.Active,"active remained after success");player.PlacementRefused=true;Build(engine,player,chest);Check(!engine.Active,"active remained after compensation start");player.PlacementRefused=false;engine.Tick();Check(!engine.Active,"active remained after compensation");});
  Test("persistent markers stay absent", "fast path never writes lease or in-use ZDO markers",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});var engine=Engine();Build(engine,player,chest);Check(chest.PersistentLease=="","persistent marker was written");});
  Test("ineligible source falls back", "busy, ownership and tool locks write nothing",()=>{var player=Player(new Dictionary<string,int>());var chest=Chest("one","Wood",4,new Dictionary<string,int>{{"Wood",3}});chest.Busy=true;var before=chest.Bytes;var engine=Engine();Check(!Build(engine,player,chest),"busy source did not fall back");Check(chest.Bytes.SequenceEqual(before),"ineligible source changed");});
  Console.WriteLine("RESULT "+passed+" fast path runtime tests passed; no Valheim process, world or clients.");return 0;
 }
}
#endif
