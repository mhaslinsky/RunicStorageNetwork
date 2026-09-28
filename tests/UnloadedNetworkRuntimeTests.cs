#if UNLOADED_NETWORK_RUNTIME_TESTS
#pragma warning disable 0649,0414
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicStorageNetwork;
using UnityEngine;
// Only Unity/game primitives are stand-ins. The registry, inactive adapters,
// request queue, source selection and save guards are the production class.
namespace UnityEngine {
 public class Object {internal bool Dead;public static implicit operator bool(Object o)=>o!=null&&!o.Dead;public static void Destroy(Object o){if(o!=null)o.Dead=true;}}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();}
 public class MonoBehaviour:Component {}
 public class Transform {public Vector3 position;public Quaternion rotation;}
 public class GameObject:Object {
  readonly List<Component> components=new List<Component>();public string name;public bool activeSelf=true;public Transform transform=new Transform();
  public GameObject(string name){this.name=name;}public void SetActive(bool active){activeSelf=active;}
  public T AddComponent<T>() where T:Component,new(){if(activeSelf)throw new Exception("Adapter must be inactive before components are added");var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
 }
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public struct Quaternion {}
 public static class Mathf {public static int CeilToInt(float n)=>(int)Math.Ceiling(n);}
 public static class Time {public static float unscaledTime;}
}
namespace HarmonyLib {
 public enum MethodType { Normal }
 public static class Priority {public const int First=0,Last=1;}
 public class HarmonyMethod {public int priority;public HarmonyMethod(Type t,string n){}}
 public class Harmony {public void Patch(MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}}
 public static class AccessTools {public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);public static MethodInfo Method(Type t,string n,Type[] a)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,a,null);}
}
static class Strings {public static int GetStableHashCode(this string s){unchecked {int h=17;foreach(char c in s)h=h*31+c;return h;}}}
struct ZDOID {internal int Id;}
struct Vector2s {public int x,y;public Vector2s(int x,int y){this.x=x;this.y=y;}public static bool operator ==(Vector2s a,Vector2s b)=>a.x==b.x&&a.y==b.y;public static bool operator !=(Vector2s a,Vector2s b)=>!(a==b);public override bool Equals(object o)=>o is Vector2s v&&v==this;public override int GetHashCode()=>x*397^y;}
static class ZoneSystem {public static Vector2s GetZone(Vector3 p)=>new Vector2s((int)Math.Floor(p.x/64),(int)Math.Floor(p.z/64));}
static class ZDOVars {public const int s_creator=1,s_enabled=2,s_permitted=3,s_items=4;}
class ZDO {
 public ZDOID m_uid;public uint DataRevision;public long Owner=7,Creator=1;public int Schema=1,Prefab;public bool Valid=true,Guard;
 public string Network="network";public Vector3 Position;public byte[] Bytes=Array.Empty<byte>();
 public bool IsValid()=>Valid;public int GetPrefab()=>Prefab;public Vector3 GetPosition()=>Position;public Quaternion GetRotation()=>new Quaternion();
 public long GetLong(int key,long fallback)=>Creator;public long GetLong(string key,long fallback)=>fallback;
 public int GetInt(string key,int fallback)=>Schema;public int GetInt(int key,int fallback)=>0;
 public bool GetBool(int key,bool fallback)=>Guard;public string GetString(string key,string fallback)=>Network;
 public long GetOwner()=>Owner;public void SetOwner(long id){Owner=id;}public byte[] GetByteArray(int key)=>Bytes;
 public void Set(int key,byte[] bytes){Bytes=bytes;DataRevision++;}
}
class ZDOMan {public static ZDOMan instance;internal Dictionary<ZDOID,ZDO> m_objectsByID=new Dictionary<ZDOID,ZDO>();}
class ZNet:UnityEngine.Object {public static ZNet instance;public static bool IsSinglePlayer=true,m_loadError;public static long GetUID()=>7;}
class ZNetView:MonoBehaviour {internal ZDO m_zdo;public ZDO GetZDO()=>m_zdo;}
class ZNetScene:UnityEngine.Object {
 public static ZNetScene instance;public List<GameObject> m_prefabs=new List<GameObject>();public Dictionary<ZDOID,GameObject> Live=new Dictionary<ZDOID,GameObject>();
 public GameObject FindInstance(ZDOID id)=>Live.TryGetValue(id,out var g)?g:null;
 public bool AreaLoaded=true;public bool IsAreaReady(Vector3 p)=>AreaLoaded;
}
class Piece:MonoBehaviour {internal ZNetView m_nview;internal long m_creator;}
class PrivateArea:MonoBehaviour {public float m_radius=32;}
class Container:MonoBehaviour {
 public enum PrivacySetting {Public,Private}
 public string m_name;public int m_width=4,m_height=2;public object m_bkg;public PrivacySetting m_privacy;
 internal ZNetView m_nview;internal Piece m_piece;internal Inventory m_inventory;internal uint m_lastRevision;
 public Inventory GetInventory()=>m_inventory;public bool IsInUse()=>false;
}
class ZPackage {byte[] bytes;public ZPackage(){}public ZPackage(byte[] data){bytes=data;}public byte[] GetArray()=>bytes;internal void Put(byte[] data){bytes=data;}}
class Inventory {
 internal byte[] Bytes=Array.Empty<byte>();internal static bool DropUnknown;
 public Inventory(string n,object b,int w,int h){}public void Load(ZPackage p){Bytes=(byte[])p.GetArray().Clone();if(DropUnknown)Bytes=new byte[]{0};}
 public void Save(ZPackage p){p.Put((byte[])Bytes.Clone());}public void RemoveAll(){Bytes=Array.Empty<byte>();}
}
namespace RunicStorageNetwork {
 class ConfigEntry {public bool Value;}
 static class Plugin {internal static ConfigEntry ExperimentalUnloadedNetworks=new ConfigEntry();internal static List<string> Messages=new List<string>();internal static void Info(string s)=>Messages.Add(s);internal static void Error(string s,Exception e){throw new Exception(s,e);}}
 class Core:MonoBehaviour {internal static HashSet<Core> Live=new HashSet<Core>();internal ZNetView view;internal void Detach(){Live.Remove(this);}}
 class NetworkMember:MonoBehaviour {internal const string NetworkKey="network",SchemaKey="schema";internal ZNetView View;internal bool Valid=>R.Valid(View);internal string Network=>View.GetZDO().Network;}
 static class R {
  internal static T Get<T>(object o,string key)=>(T)o.GetType().GetField(key,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
  internal static void Set(object o,string key,object value)=>o.GetType().GetField(key,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);
  internal static ZNetView View(Component c)=>c?c.GetComponent<ZNetView>():null;
  internal static bool Valid(ZNetView v)=>v&&v.GetZDO()!=null&&v.GetZDO().IsValid();internal static string Key(ZDOID id)=>id.Id.ToString();
 }
 static class ContainerPolicy {internal static bool Eligible(string name)=>name!="ExcludedChest";}
 static class RemoteContext {
  internal static ZDO Data(ZDOID id)=>ZDOMan.instance.m_objectsByID.TryGetValue(id,out var z)&&z.Valid?z:null;
  internal static GameObject Prefab(ZDO z)=>ZNetScene.instance.m_prefabs.FirstOrDefault(p=>p.name.GetStableHashCode()==z.Prefab);
 }
 static class StorageIndex {internal static Func<bool> DemandDriven,HasDemand;internal static Func<Container,bool> AreaReady;internal static Func<Container,Exception,bool> ReadFailure;internal static void Changed(string key){}}
 static class CraftInspection {internal static Func<ZDOID,Container> FindContainer;}
 class Number {public double X,Y,Z;public double Distance2(Number p)=>Math.Pow(X-p.X,2)+Math.Pow(Y-p.Y,2)+Math.Pow(Z-p.Z,2);}
 class Node {internal string Id,Network;internal Number Position;internal double Storage=20;}
 class Graph {internal Dictionary<string,Node> Nodes=new Dictionary<string,Node>();internal Dictionary<string,int> Hops=new Dictionary<string,int>();}
 static class Topology {internal static HashSet<NetworkMember> Members=new HashSet<NetworkMember>();internal static Graph Graph=new Graph();internal static int Changes;internal static void Dirty(){Changes++;}internal static Number Position(Vector3 p)=>new Number{X=p.x,Y=p.y,Z=p.z};}
 static class Transport {internal class Lease {internal Container Container;}internal static Dictionary<Inventory,Lease> Leases=new Dictionary<Inventory,Lease>();}
}
static class UnloadedNetworkRuntimeTests {
 static int passed;static ZDO core,chest;static GameObject chestPrefab;
 static object Call(string method,params object[] args){try{return typeof(UnloadedNetworks).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static GameObject Prefab<T>(string name) where T:Component,new(){var g=new GameObject(name);g.SetActive(false);g.AddComponent<T>();ZNetScene.instance.m_prefabs.Add(g);return g;}
 static ZDO Record(string name,int id,float x){var z=new ZDO{m_uid=new ZDOID{Id=id},Prefab=name.GetStableHashCode(),Position=new Vector3(x,0,0)};ZDOMan.instance.m_objectsByID[z.m_uid]=z;return z;}
 static void Setup(){
  Call("Shutdown");ZNet.instance=new ZNet();ZNet.IsSinglePlayer=true;ZNet.m_loadError=false;ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();Plugin.ExperimentalUnloadedNetworks.Value=true;Plugin.Messages.Clear();Transport.Leases.Clear();Core.Live.Clear();Topology.Graph=new Graph();Topology.Changes=0;Time.unscaledTime=0;Inventory.DropUnknown=false;
  Prefab<Core>("RSN_NetworkCore");Prefab<NetworkMember>("RSN_RunicRelay");chestPrefab=Prefab<Container>("ModdedDrawer");Prefab<Container>("ExcludedChest");
  core=Record("RSN_NetworkCore",1,0);chest=Record("ModdedDrawer",2,5);chest.Bytes=new byte[]{109,3,7,8};
 }
 static void Start(){Call("Bootstrap");Call("WorldLoaded");UnloadedNetworks.Prepare();for(int i=0;i<20;i++)UnloadedNetworks.Tick();}
 static Container LiveChest(){var g=new GameObject("ModdedDrawer");g.SetActive(false);g.AddComponent<ZNetView>().m_zdo=chest;var c=g.AddComponent<Container>();c.m_inventory=new Inventory("",null,4,2);ZNetScene.instance.Live[chest.m_uid]=g;return c;}
 static Container Request(){
  var c=UnloadedNetworks.FindCore(core.m_uid);Topology.Graph.Nodes["1"]=new Node{Id="1",Network="network",Position=Topology.Position(core.Position)};Topology.Graph.Hops["1"]=0;
  UnloadedNetworks.Request(c);for(int i=0;i<20;i++)UnloadedNetworks.Tick();return UnloadedNetworks.FindContainer(chest.m_uid);
 }
 static void Load(Container c){var args=new object[]{c,false};Call("LoadReplica",args);}
 static bool Refuses(Action work){try{work();return false;}catch(InvalidOperationException){return true;}}
 static void Test(string name,Action test){Setup();test();passed++;Console.WriteLine("PASS unloaded runtime "+name);}
 public static int Main(){try{
  Test("disabled setting creates no adapters",()=>{Plugin.ExperimentalUnloadedNetworks.Value=false;Call("Bootstrap");UnloadedNetworks.Prepare();UnloadedNetworks.Tick();Check(!UnloadedNetworks.Enabled&&Core.Live.Count==0,"disabled mode changed world");});
  Test("multiplayer cannot enable offline writes",()=>{ZNet.IsSinglePlayer=false;Call("Bootstrap");Check(!UnloadedNetworks.Enabled,"multiplayer enabled");});
  Test("cold start finds core without any live instances",()=>{Start();Check(UnloadedNetworks.FindCore(core.m_uid)&&ZNetScene.instance.Live.Count==0,"requires loaded core");Check(Core.Live.All(c=>!c.gameObject.activeSelf),"active adapter");});
  Test("chunked save arriving after scene Awake is indexed without visiting core",()=>{ZDOMan.instance.m_objectsByID.Clear();Call("Bootstrap");Check(!UnloadedNetworks.Prepare(),"indexed before save loaded");ZDOMan.instance.m_objectsByID[core.m_uid]=core;ZDOMan.instance.m_objectsByID[chest.m_uid]=chest;Call("WorldLoaded");UnloadedNetworks.Prepare();for(int i=0;i<20;i++)UnloadedNetworks.Tick();Check(UnloadedNetworks.FindCore(core.m_uid)&&Request()&&ZNetScene.instance.Live.Count==0,"late saved records missed");});
  Test("completed world index is not rebuilt on repeated notification",()=>{Start();int before=Topology.Changes;Call("WorldLoaded");Check(Topology.Changes==before,"repeated full scan");});
  Test("failed world load cannot index a partial save",()=>{ZNet.m_loadError=true;Start();Check(!UnloadedNetworks.Prepare()&&!UnloadedNetworks.FindCore(core.m_uid),"indexed failed load");});
  Test("startup indexes metadata without eagerly creating storage",()=>{Start();Check(!UnloadedNetworks.FindContainer(chest.m_uid),"eager storage load");});
  Test("modded standard containers share the normal eligibility filter",()=>{Start();var c=Request();Check(c&&c.gameObject.name=="ModdedDrawer"&&!c.gameObject.activeSelf,"modded storage excluded");});
  Test("denied container is not connected",()=>{chest.Prefab="ExcludedChest".GetStableHashCode();Start();Check(!Request(),"excluded storage bypassed");});
  Test("unplaced nodes cannot stall discovery",()=>{var ghost=Record("RSN_RunicRelay",3,10);ghost.Creator=0;Start();Check(UnloadedNetworks.Prepare(),"unplaced record blocked initialization");});
  Test("idle work queue does not create adapters",()=>{Start();var c=UnloadedNetworks.FindCore(core.m_uid);Topology.Graph.Nodes["1"]=new Node{Id="1",Network="network",Position=Topology.Position(core.Position)};Topology.Graph.Hops["1"]=0;UnloadedNetworks.Request(c);Time.unscaledTime=1;UnloadedNetworks.Tick();Check(!UnloadedNetworks.FindContainer(chest.m_uid),"idle queue ran");});
  Test("ordinary node data revision does not rebuild graph",()=>{Start();int before=Topology.Changes;core.DataRevision++;Call("Changed",core);Check(Topology.Changes==before,"health/other data dirtied graph");});
  Test("node movement updates retained position and invalidates graph",()=>{Start();int before=Topology.Changes;core.Position=new Vector3(40,0,0);Call("Changed",core);Check(Topology.Changes>before&&UnloadedNetworks.FindCore(core.m_uid).transform.position.x==40,"movement missed");});
  Test("offline inventory round trip preserves all bytes",()=>{Start();var c=Request();Load(c);Check(c.GetInventory().Bytes.SequenceEqual(chest.Bytes),"metadata changed");});
  Test("unknown or lossy inventory format refuses offline read",()=>{Start();var c=Request();var before=(byte[])chest.Bytes.Clone();Inventory.DropUnknown=true;Check(Refuses(()=>Load(c))&&chest.Bytes.SequenceEqual(before),"lossy read wrote data");});
  Test("successful offline save updates authoritative record",()=>{Start();var c=Request();Load(c);c.GetInventory().Bytes=new byte[]{109,2,7,8};Call("SaveReplica",c);Check(chest.Bytes.SequenceEqual(new byte[]{109,2,7,8}),"not saved");});
  Test("concurrent record change refuses stale overwrite",()=>{Start();var c=Request();Load(c);chest.Bytes=new byte[]{109,1,4};Check(Refuses(()=>Call("SaveReplica",c))&&chest.Bytes.SequenceEqual(new byte[]{109,1,4}),"overwrote external change");});
  Test("unpaid conflict can release without overwriting saved items",()=>{Start();var c=Request();Load(c);chest.Bytes=new byte[]{109,1,4};c.GetInventory().Bytes=new byte[]{109,2,7};Check(Refuses(()=>Call("SaveReplica",c))&&UnloadedNetworks.DiscardUnpaid(c,false),"failed write stayed locked");Load(c);Check(c.GetInventory().Bytes.SequenceEqual(chest.Bytes),"discarded inventory was reused");});
  Test("saved debit cannot be discarded even before acknowledgement",()=>{Start();var c=Request();Load(c);c.GetInventory().Bytes=new byte[]{109,2,7};Call("SaveReplica",c);Check(!UnloadedNetworks.DiscardUnpaid(c,false)&&!UnloadedNetworks.DiscardUnpaid(c,true),"persisted debit skipped rollback");});
  Test("loaded chest takes over without duplicate source",()=>{Start();var proxy=Request();var live=new GameObject("ModdedDrawer");live.SetActive(false);live.AddComponent<ZNetView>().m_zdo=chest;var c=live.AddComponent<Container>();c.m_inventory=new Inventory("",null,4,2);ZNetScene.instance.Live[chest.m_uid]=live;Check(UnloadedNetworks.FindContainer(chest.m_uid)==c&&!UnloadedNetworks.Containers.Contains(proxy),"duplicate adapters");});
  Test("retained live chest remains usable after its zone unloads",()=>{Start();var live=LiveChest();ZNetScene.instance.AreaLoaded=false;chest.Owner=0;Check(Request()==live&&UnloadedNetworks.CanUseUnloaded(live)&&UnloadedNetworks.SourceReady(live),"live chest rejected with unconfirmed loaded area");Check(chest.Owner==ZNet.GetUID()&&UnloadedNetworks.KeepOwner(chest,0),"retained source lost single-player owner");Check(!UnloadedNetworks.Containers.Any(),"duplicated retained inventory");});
  Test("untracked live chest cannot bypass readiness",()=>{Start();var live=LiveChest();var unknown=new ZDO{m_uid=new ZDOID{Id=99},Prefab=chest.Prefab};live.m_nview=null;live.GetComponent<ZNetView>().m_zdo=unknown;ZNetScene.instance.Live[unknown.m_uid]=live.gameObject;ZDOMan.instance.m_objectsByID[unknown.m_uid]=unknown;ZNetScene.instance.AreaLoaded=false;Check(!UnloadedNetworks.CanUseUnloaded(live)&&!UnloadedNetworks.SourceReady(live),"untracked source bypassed readiness");});
  Test("stale live instance cannot borrow replacement record with same id",()=>{Start();var live=LiveChest();ZNetScene.instance.AreaLoaded=false;ZDOMan.instance.m_objectsByID[chest.m_uid]=new ZDO{m_uid=chest.m_uid,Prefab=chest.Prefab};Check(!UnloadedNetworks.CanUseUnloaded(live),"stale instance accepted");});
  Test("disabled experiment keeps normal area readiness for live sources",()=>{Plugin.ExperimentalUnloadedNetworks.Value=false;Call("Bootstrap");Call("WorldLoaded");var live=LiveChest();ZNetScene.instance.AreaLoaded=false;Check(!UnloadedNetworks.CanUseUnloaded(live)&&!UnloadedNetworks.SourceReady(live),"disabled behavior changed");ZNetScene.instance.AreaLoaded=true;Check(UnloadedNetworks.SourceReady(live),"normal loaded source rejected");});
  Test("in-flight lease retains original offline inventory",()=>{Start();var proxy=Request();Transport.Leases[proxy.GetInventory()]=new Transport.Lease{Container=proxy};ZNetScene.instance.Live[chest.m_uid]=chestPrefab;Check(UnloadedNetworks.FindContainer(chest.m_uid)==proxy,"lease source switched");});
  Test("active real chest lease is pinned until release",()=>{Start();var live=new GameObject("ModdedDrawer");live.SetActive(false);live.AddComponent<ZNetView>().m_zdo=chest;var c=live.AddComponent<Container>();var inv=new Inventory("",null,4,2);Transport.Leases[inv]=new Transport.Lease{Container=c};var list=new List<ZDO>();Call("PinTransactions",list);Check(list.Count==1&&list[0]==chest,"leased live source unloaded");Transport.Leases.Clear();list.Clear();Call("PinTransactions",list);Check(list.Count==0,"pin survived release");});
  Test("destroying core removes retained reference",()=>{Start();var c=UnloadedNetworks.FindCore(core.m_uid);Call("Destroyed",core.m_uid);Check(!UnloadedNetworks.FindCore(core.m_uid)&&!R.Valid(R.View(c)),"ghost core survived");});
  Test("shutdown clears previous world adapters",()=>{Start();Request();Call("Shutdown");Check(!UnloadedNetworks.Enabled&&Core.Live.Count==0&&!UnloadedNetworks.FindContainer(chest.m_uid),"world state leaked");});
  Test("configuration change takes effect only after world reload",()=>{Start();Plugin.ExperimentalUnloadedNetworks.Value=false;Check(UnloadedNetworks.Enabled,"mid-operation toggle");Call("Bootstrap");Check(!UnloadedNetworks.Enabled,"reload ignored setting");});
  Console.WriteLine("Unloaded network runtime tests: "+passed+" passed (game stand-ins)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
