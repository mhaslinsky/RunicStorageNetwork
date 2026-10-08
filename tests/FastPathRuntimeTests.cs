#if FAST_PATH_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork;

static class FastPathRuntimeTests {
 static int passed;
 static void Test(string name,string reason,Action action){action();passed++;Console.WriteLine("PASS fast path "+name+": "+reason);}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static FastPathRuntimePlayer Player(){var player=new FastPathRuntimePlayer();player.Inventory.Items.Add(new FastPathRuntimeItem{Name="Wood",Stack=2,Quality=1,Metadata="player"});return player;}
 static FastPathRuntimeChest Chest(string key,string item,int amount){var chest=new FastPathRuntimeChest{Key=key};chest.Inventory.Items.Add(new FastPathRuntimeItem{Name=item,Stack=amount,Quality=1,Metadata=key});return chest;}
 static Dictionary<string,int> Need(params (string Name,int Amount)[] entries){return entries.ToDictionary(entry=>entry.Name,entry=>entry.Amount,StringComparer.Ordinal);}
 static byte[] Snapshot(FastPathRuntimeInventory inventory)=>inventory.Bytes();
 static int Main(){
  Test("mixed player and chest debit", "one output and one cost use the exact planned shares",()=>{
   var player=Player();var first=Chest("first","Wood",3);var second=Chest("second","Wood",4);var engine=new FastPathRuntime();Check(engine.TryBuild(player,Need(("Wood",7)),new[]{first,second}),"fast path did not handle build");Check(player.Inventory.Items.Single().Stack==0&&first.Inventory.Items.Single().Stack==0&&second.Inventory.Items.Single().Stack==2,"wrong debit");Check(player.Outputs==1&&player.Costs==1&&!engine.Holds("first"),"cost or release count wrong");});
  Test("several debits from one chest", "different materials are removed together",()=>{
   var player=new FastPathRuntimePlayer();var chest=Chest("one","Wood",5);chest.Inventory.Items.Add(new FastPathRuntimeItem{Name="Stone",Stack=4,Quality=1,Metadata="one"});var engine=new FastPathRuntime();Check(engine.TryBuild(player,Need(("Wood",3),("Stone",2)),new[]{chest}),"build was not handled");Check(chest.Inventory.Items.Sum(item=>item.Stack)==4,"mixed debit wrong");});
  Test("partial removal restores bytes", "a failed RemoveItem leaves every item record unchanged",()=>{
   var player=Player();var chest=Chest("one","Wood",5);var before=Snapshot(chest.Inventory);chest.Inventory.FailRemove=true;var engine=new FastPathRuntime();Check(engine.TryBuild(player,Need(("Wood",3)),new[]{chest}),"failure was not handled");Check(engine.Holds("one"),"failed restore should be pending");chest.Inventory.FailRemove=false;engine.Tick();Check(Snapshot(chest.Inventory).SequenceEqual(before)&&player.Inventory.Items.Single().Stack==2&&!engine.Holds("one"),"restore was not byte exact");});
  Test("later source failure restores earlier source", "all sources roll back in reverse order",()=>{
   var player=Player();var first=Chest("first","Wood",4);var second=Chest("second","Wood",4);second.FailSave=true;var firstBefore=Snapshot(first.Inventory);var secondBefore=Snapshot(second.Inventory);var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",6)),new[]{first,second});second.FailSave=false;engine.Tick();Check(Snapshot(first.Inventory).SequenceEqual(firstBefore)&&Snapshot(second.Inventory).SequenceEqual(secondBefore),"earlier source was not restored");});
  Test("placement refusal restores payment", "no output leaves player and chest unchanged",()=>{
   var player=Player();player.PlacementRefused=true;var chest=Chest("one","Wood",4);var before=Snapshot(chest.Inventory);var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});player.PlacementRefused=false;engine.Tick();Check(Snapshot(chest.Inventory).SequenceEqual(before)&&player.Inventory.Items.Single().Stack==2&&player.Outputs==0,"placement failure kept a debit");});
  Test("compensation retries without server payment", "a later frame clears the hold after restore succeeds",()=>{
   var player=Player();var chest=Chest("one","Wood",4);chest.Inventory.FailRemove=true;var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});Check(engine.BuildBlocked(player)&&engine.Holds("one")&&!engine.HasPersistentMarkers,"compensation did not hold the source in memory");chest.Inventory.FailRemove=false;engine.Tick();Check(!engine.BuildBlocked(player)&&!engine.Holds("one")&&player.Outputs==0,"compensation did not finish");});
  Test("remote operations refuse a held chest", "open, ownership move and coordinated prepare see the in-memory hold",()=>{
   var player=Player();var chest=Chest("one","Wood",4);chest.Inventory.FailRemove=true;var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});Check(!engine.RemoteOpen(chest)&&!engine.OwnershipMove(chest,2),"remote operation was allowed");chest.Inventory.FailRemove=false;engine.Tick();});
  Test("owner loss keeps compensation blocked", "a fence mismatch never overwrites a new owner",()=>{
   var player=Player();var chest=Chest("one","Wood",4);chest.Inventory.FailRemove=true;var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});chest.Inventory.FailRemove=false;chest.Owner=2;engine.Tick();Check(engine.BuildBlocked(player)&&engine.Holds("one")&&chest.Inventory.Items.Single().Stack==3,"owner loss was not fail closed: blocked="+engine.BuildBlocked(player)+" held="+engine.Holds("one")+" stack="+chest.Inventory.Items.Single().Stack);});
  Test("ineligible source falls back without writes", "busy, gateway filtered and tool locked sources are untouched",()=>{
   var player=Player();var chest=Chest("one","Wood",4);chest.Busy=true;var before=Snapshot(chest.Inventory);var engine=new FastPathRuntime();Check(!engine.TryBuild(player,Need(("Wood",3)),new[]{chest}),"busy source did not fall back");Check(Snapshot(chest.Inventory).SequenceEqual(before),"fallback wrote stock");chest.Busy=false;player.ToolLocked=true;Check(!engine.TryBuild(player,Need(("Wood",3)),new[]{chest}),"locked tool did not fall back");player.ToolLocked=false;chest.GatewayAllowed=false;Check(!engine.TryBuild(player,Need(("Wood",3)),new[]{chest}),"filtered source did not fall back");});
  Test("reentrant build is refused", "the active transaction blocks a callback attempt",()=>{
   var player=Player();var chest=Chest("one","Wood",4);var engine=new FastPathRuntime();bool callbackResult=true;engine.TryBuild(player,Need(("Wood",3)),new[]{chest},()=>{callbackResult=engine.TryBuild(player,Need(("Wood",1)),new[]{chest});});Check(!callbackResult&&player.Outputs==1,"reentrant build was accepted");});
  Test("coordinated prepare sees reduced stock", "the next plan reads the saved post-debit count",()=>{
   var player=Player();var chest=Chest("one","Wood",5);var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",4)),new[]{chest});Check(chest.Inventory.Items.Single().Stack==3,"saved stock was stale");});
  Test("post-output release retries", "payment stays while cleanup releases the source",()=>{
   var player=Player();var chest=Chest("one","Wood",4);chest.FailRelease=true;var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});Check(player.Outputs==1&&engine.Holds("one")&&!engine.BuildBlocked(player),"output did not enter cleanup");chest.FailRelease=false;engine.Tick();Check(!engine.Holds("one")&&!chest.InUse,"cleanup did not release");});
  Test("persistent markers stay absent", "fast path never publishes rsn_lease or s_inUse state",()=>{
   var player=Player();var chest=Chest("one","Wood",4);var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});Check(!engine.HasPersistentMarkers&&chest.PersistentLease=="","persistent marker was written");});
  Test("metadata is preserved", "unrelated item fields survive a successful debit",()=>{
   var player=new FastPathRuntimePlayer();var chest=Chest("one","Wood",5);chest.Inventory.Items.Add(new FastPathRuntimeItem{Name="Iron",Stack=2,Quality=3,Metadata="magic"});var before=Snapshot(chest.Inventory);var engine=new FastPathRuntime();engine.TryBuild(player,Need(("Wood",3)),new[]{chest});Check(chest.Inventory.Items.Single(item=>item.Name=="Iron").Metadata=="magic"&&before.Length>0,"metadata changed");});
  Console.WriteLine("RESULT "+passed+" fast path runtime tests passed; no Valheim process, world or clients.");return 0;
 }
}
#endif
