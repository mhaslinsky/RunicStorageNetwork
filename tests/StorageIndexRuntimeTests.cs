#if STORAGE_INDEX_RUNTIME_TESTS
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork;
using RunicStorageNetwork.Logic;
namespace UnityEngine {
 public class Object {public bool Destroyed;public static implicit operator bool(Object o)=>o!=null&&!o.Destroyed;}
 public class Transform {public object position;}
 public static class Time {public static float unscaledTime;}
}
struct ZDOID {public int Value;}
class ZDO {public ZDOID m_uid;public uint DataRevision=1;public long Owner=7;public long GetOwner()=>Owner;public int GetInt(int key)=>0;}
static class ZDOVars {public const int s_inUse=1;}
class ZNetView {public ZDO Zdo;public ZDO GetZDO()=>Zdo;}
class Inventory {public Action m_onChanged;public List<Stock> Items=new List<Stock>();public void Changed()=>m_onChanged?.Invoke();}
class Container:UnityEngine.Object {public Inventory Inventory=new Inventory();public ZNetView View;public UnityEngine.Transform transform=new UnityEngine.Transform();public Inventory GetInventory()=>Inventory;public bool IsInUse()=>false;}
class ZNetScene {public static ZNetScene instance=new ZNetScene();public bool IsAreaReady(object point)=>true;}
namespace RunicStorageNetwork {
 class Core:UnityEngine.Object {internal List<Container> Pool=new List<Container>();}
 static class R {internal static ZNetView View(Container c)=>c?.View;internal static bool Valid(ZNetView v)=>v?.Zdo!=null;internal static string Key(ZDOID id)=>id.Value.ToString();internal static object Call(Container c,string method)=>null;}
 static class Access {internal static bool Container(Container c,long player,Core core,out string reason,bool ownLease){reason=null;return c&&core.Pool.Contains(c);}}
 static class Transport {internal static bool Locked(Inventory i)=>false;}
 static class Integrations {internal static bool IsBusy(Inventory i)=>false;}
 static class Stockroom {internal static int Reads;internal static bool Fail;internal static List<Stock> Preview(Container c){if(Fail)throw new InvalidOperationException("unreadable inventory");Reads++;return c.Inventory.Items.ToList();}}
}
static class StorageIndexRuntimeTests {
 static int passed;static Core core;static bool demand=true,areaReady=true,handleFailure;static void EnableOffline(){StorageIndex.Offline=new StorageIndex.UnloadedMode(()=>true,()=>demand,c=>areaReady,(c,e)=>handleFailure);}
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static Container Add(int id,string name="Wood",int amount=10){var c=new Container{View=new ZNetView{Zdo=new ZDO{m_uid=new ZDOID{Value=id}}}};if(amount>0)c.Inventory.Items.Add(new Stock(id.ToString(),name,1,amount));core.Pool.Add(c);StorageIndex.Register(c);return c;}
 static List<Stock> Query(string name="Wood")=>StorageIndex.Query(core,1,new[]{new Need(name,1)});
 static void Drain(){for(int i=0;i<100;i++)StorageIndex.Tick();}
 static void Test(string name,Action body){StorageIndex.Clear();StorageIndex.Offline=null;demand=areaReady=true;handleFailure=false;core=new Core();UnityEngine.Time.unscaledTime=0;Stockroom.Reads=0;Stockroom.Fail=false;body();passed++;Console.WriteLine("PASS storage runtime "+name);}
 public static int Main(){try{
  Test("95 chests are indexed once and unchanged frames do not recount",()=>{for(int i=1;i<=95;i++)Add(i);Drain();Check(Query().Sum(s=>s.Amount)==950,"incomplete index");int reads=Stockroom.Reads;Drain();Check(Stockroom.Reads==reads,"unchanged inventories recounted");});
  Test("inventory event rereads only the changed chest",()=>{var a=Add(1);Add(2);Drain();Stockroom.Reads=0;a.Inventory.Items[0].Amount=40;a.Inventory.Changed();Drain();Check(Stockroom.Reads==1&&Query().Sum(s=>s.Amount)==50,"event did not isolate source");});
  Test("synchronized revision discovers resource in formerly empty chest",()=>{var a=Add(1,amount:0);Drain();a.Inventory.Items.Add(new Stock("1","Wood",1,40));a.View.Zdo.DataRevision++;Drain();Check(Query().Single().Amount==40,"new synchronized item missed");});
  Test("owner handoff rereads only that inventory",()=>{var a=Add(1);Add(2);Drain();Stockroom.Reads=0;a.View.Zdo.Owner=8;Drain();Check(Stockroom.Reads==1,"owner epoch not refreshed");});
  Test("destroyed chest stops contributing and unsubscribes",()=>{var a=Add(1);Drain();a.Destroyed=true;Drain();Check(Query().Count==0&&a.Inventory.m_onChanged==null,"destroyed source leaked");});
  Test("recreated source replaces old instance without duplicate stock",()=>{var a=Add(1);Drain();core.Pool.Remove(a);Add(1,amount:4);Drain();Check(Query().Single().Amount==4&&a.Inventory.m_onChanged==null,"recreated source duplicated");});
  Test("new contents from a mod without events are found by bounded reconciliation",()=>{var a=Add(1);Drain();a.Inventory.Items[0].Amount=2;UnityEngine.Time.unscaledTime=31;Drain();Check(Query().Single().Amount==2,"silent mutation never found");});
  Test("selected resource queries exclude unrelated sources",()=>{Add(1);Add(2,"Iron");Drain();Check(StorageIndex.Candidates(core,1,new[]{new Need("Wood",1)},false).Count()==1,"queried irrelevant chest");});
  Test("shortage discovery includes formerly empty containers",()=>{Add(1);Add(2,amount:0);Drain();Check(StorageIndex.Candidates(core,1,new[]{new Need("Wood",30)},true).Count()==2,"empty candidate omitted");});
  Test("access and network membership are checked at query time",()=>{var a=Add(1);Drain();core.Pool.Remove(a);Check(Query().Count==0,"foreign network stock leaked");});
  Test("opening station reconciliation refreshes contents",()=>{var a=Add(1);Drain();a.Inventory.Items[0].Amount=25;StorageIndex.Reconcile(core);Drain();Check(Query().Single().Amount==25,"open station reused silent mutation");});
  Test("world clear detaches all listeners",()=>{var a=Add(1);Drain();StorageIndex.Clear();Check(a.Inventory.m_onChanged==null&&Query().Count==0,"world state retained");});
  Test("disabled optional scheduler cannot intercept normal inventory reads",()=>{StorageIndex.Offline=new StorageIndex.UnloadedMode(()=>false,()=>throw new Exception("demand queried"),c=>throw new Exception("offline readiness used"),(c,e)=>throw new Exception("offline error handler used"));var a=Add(1);Drain();a.Inventory.Items[0].Amount=25;StorageIndex.Reconcile(core);Drain();Check(Query().Single().Amount==25,"normal reconciliation changed");a.Inventory.Items[0].Amount=3;UnityEngine.Time.unscaledTime=31;Drain();Check(Query().Single().Amount==3,"30-second normal reconciliation disabled");StorageIndex.Fresh("1");});
  Test("normal inventory errors are not swallowed by experimental handling",()=>{Add(1);Drain();Stockroom.Fail=true;bool threw=false;try{StorageIndex.Fresh("1");}catch(InvalidOperationException){threw=true;}Check(threw,"normal failure was hidden");});
  Test("server opt-out restores periodic audits after provisional experimental reads",()=>{bool active=true;StorageIndex.Offline=new StorageIndex.UnloadedMode(()=>active,()=>true,c=>true,(c,e)=>false);var a=Add(1);Drain();StorageIndex.Offline.Suspend();active=false;Drain();a.Inventory.Items[0].Amount=7;UnityEngine.Time.unscaledTime=31;Drain();Check(Query().Single().Amount==7,"server opt-out kept infinite audit interval");});
  Test("experimental idle never reads a queued chest",()=>{EnableOffline();demand=false;Add(1);Drain();Check(Stockroom.Reads==0,"idle work");demand=true;Drain();Check(Stockroom.Reads==1,"request not serviced");});
  Test("experimental 95 chests are read once with no periodic audits",()=>{EnableOffline();for(int i=1;i<=95;i++)Add(i);Drain();Check(Stockroom.Reads==95&&Query().Sum(s=>s.Amount)==950,"incomplete request");UnityEngine.Time.unscaledTime=900;Drain();Check(Stockroom.Reads==95,"periodic rescan");});
  Test("experimental repeated events coalesce to one source update",()=>{EnableOffline();var a=Add(1);Add(2);Drain();Stockroom.Reads=0;a.Inventory.Items[0].Amount=25;for(int i=0;i<20;i++)a.Inventory.Changed();Drain();Check(Stockroom.Reads==1&&Query().Sum(s=>s.Amount)==35,"event amplification");});
  Test("experimental opening reconciles missed revision events",()=>{EnableOffline();var a=Add(1,amount:0);Drain();a.Inventory.Items.Add(new Stock("1","Wood",1,50));a.View.Zdo.DataRevision++;StorageIndex.Reconcile(core);Drain();Check(Query().Single().Amount==50,"new resource missed");});
  Test("experimental queued old instance cannot overwrite replacement",()=>{EnableOffline();var a=Add(1);core.Pool.Remove(a);Add(1,amount:4);Drain();Check(Query().Single().Amount==4&&Stockroom.Reads==1,"stale queue entry applied");});
  Test("experimental source waits for area readiness without losing queued update",()=>{EnableOffline();areaReady=false;Add(1);Drain();Check(Stockroom.Reads==0,"read unready area");areaReady=true;Drain();Check(Query().Single().Amount==10&&Stockroom.Reads==1,"readiness update lost");});
  Test("refused offline read removes formerly available counts and can recover",()=>{EnableOffline();Add(1);Drain();Stockroom.Fail=true;handleFailure=true;StorageIndex.Changed("1");Drain();Check(Query().Count==0&&!StorageIndex.Ready(core),"stale counts survived refusal");Stockroom.Fail=false;StorageIndex.Reconcile(core);Drain();Check(Query().Single().Amount==10,"recovery failed");});
  Console.WriteLine("Storage runtime tests: "+passed+" passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
