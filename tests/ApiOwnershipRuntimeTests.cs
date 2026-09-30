#if API_OWNERSHIP_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicStorageNetwork;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;
using UnityEngine;

namespace UnityEngine {
 class Object {public static implicit operator bool(Object v)=>v!=null;}
 class Component:Object {public Container Owner;public T GetComponent<T>()where T:class=>Owner as T;}
 struct Vector3 {public float x,y,z;}
}
namespace HarmonyLib {
 class Harmony {public void Patch(MethodInfo method,HarmonyMethod prefix=null){if(method==null)throw new Exception("missing patch");}}
 class HarmonyMethod {public int priority;public HarmonyMethod(Type t,string n){}}
 static class Priority {public const int First=800;}
 static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] a)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,a,null);}
}
public struct ZDOID:IEquatable<ZDOID> {
 public long User;public uint Id;public ZDOID(long user,uint id){User=user;Id=id;}public static ZDOID None=>default;
 public bool Equals(ZDOID other)=>User==other.User&&Id==other.Id;public override bool Equals(object o)=>o is ZDOID id&&Equals(id);public override int GetHashCode()=>User.GetHashCode()^(int)Id;
 public static bool operator ==(ZDOID a,ZDOID b)=>a.Equals(b);public static bool operator !=(ZDOID a,ZDOID b)=>!a.Equals(b);
}
class ZPackage {
 readonly MemoryStream stream=new MemoryStream();readonly BinaryReader reader;readonly BinaryWriter writer;
 public ZPackage(){reader=new BinaryReader(stream);writer=new BinaryWriter(stream);}public ZPackage(byte[] b):this(){stream.Write(b,0,b.Length);stream.Position=0;}
 public byte[] GetArray()=>stream.ToArray();public int GetPos()=>(int)stream.Position;public int Size()=>(int)stream.Length;public void SetPos(int p)=>stream.Position=p;
 public void Write(int v)=>writer.Write(v);public void Write(uint v)=>writer.Write(v);public void Write(ushort v)=>writer.Write(v);public void Write(long v)=>writer.Write(v);public void Write(bool v)=>writer.Write(v);public void Write(string v)=>writer.Write(v);public void Write(double v)=>writer.Write(v);
 public void Write(ZDOID id){Write(id.User);Write(id.Id);}public void Write(Vector3 v){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}public void Write(byte[] bytes){Write(bytes.Length);writer.Write(bytes);}public void Write(ZPackage p)=>Write(p.GetArray());
 public int ReadInt()=>reader.ReadInt32();public uint ReadUInt()=>reader.ReadUInt32();public ushort ReadUShort()=>reader.ReadUInt16();public long ReadLong()=>reader.ReadInt64();public bool ReadBool()=>reader.ReadBoolean();public string ReadString()=>reader.ReadString();public double ReadDouble()=>reader.ReadDouble();
 public ZDOID ReadZDOID()=>new ZDOID(ReadLong(),ReadUInt());public Vector3 ReadVector3()=>new Vector3{x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle()};public byte[] ReadByteArray()=>reader.ReadBytes(ReadInt());
}
static class ZDOVars {public const string s_items="items",s_inUse="use";}
class ZDO {
 public ZDOID m_uid;public ushort OwnerRevision;public uint DataRevision;public long Owner=7;public bool Valid=true;readonly Dictionary<string,object> values=new Dictionary<string,object>();
 public long GetOwner()=>Owner;public void SetOwner(long who){if(Owner!=who){Owner=who;OwnerRevision++;}}public bool IsValid()=>Valid;
 public int GetInt(string k,int d)=>values.TryGetValue(k,out var v)?(int)v:d;public string GetString(string k,string d)=>values.TryGetValue(k,out var v)?(string)v:d;public byte[] GetByteArray(string k)=>values.TryGetValue(k,out var v)?(byte[])v:null;
 public void Set(string k,int v){values[k]=v;DataRevision++;}public void Set(string k,string v){values[k]=v;DataRevision++;}public void Set(string k,byte[] v){values[k]=v;DataRevision++;}
}
class ZRpc {public long Peer;}
class ZNetPeer {public long m_uid;}
class ZNet:UnityEngine.Object {public static ZNet instance=new ZNet();public bool Server=true;public static long UID=7;public static long GetUID()=>UID;public bool IsServer()=>Server;public ZNetPeer GetPeer(long uid)=>uid==17?new ZNetPeer{m_uid=17}:null;}
class ZDOMan {
 public static ZDOMan instance=new ZDOMan();public readonly Dictionary<ZDOID,ZDO> Data=new Dictionary<ZDOID,ZDO>();public ZDO GetZDO(ZDOID id)=>Data.TryGetValue(id,out var z)?z:null;
 public sealed class Peer {public ZNetPeer m_peer;}
 public Peer FindPeer(ZRpc rpc)=>new Peer{m_peer=new ZNetPeer{m_uid=rpc.Peer}};
 public void RPC_ZDOData(ZRpc rpc,ZPackage p){}public void ForceSendZDO(ZDOID id){}
}
class ZNetView:UnityEngine.Object {public ZDO Data;public ZDO GetZDO()=>Data;public bool IsOwner()=>Data.Owner==ZNet.UID;}
class Inventory {public byte[] Bytes=new byte[]{1,2,3};public bool ContainsItem(ItemDrop.ItemData item)=>true;}
class ItemDrop {public class ItemData {}}
class Container:Component {
 public ZNetView View;public Inventory Inventory=new Inventory();public bool m_inUse;public uint m_lastRevision;public bool FailSave;public Container(){Owner=this;}
 public Inventory GetInventory()=>Inventory;public bool IsInUse()=>m_inUse;public void Load()=>Inventory.Bytes=View.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>();
 public void Save(){if(FailSave)throw new Exception("save");View.Data.Set(ZDOVars.s_items,Inventory.Bytes);}public bool CheckAccess(long id)=>true;
}
class ZNetScene:UnityEngine.Object {public static ZNetScene instance=new ZNetScene();public readonly Dictionary<ZDOID,Container> Live=new Dictionary<ZDOID,Container>();public Container FindInstance(ZDOID id)=>Live.TryGetValue(id,out var c)?c:null;public void RemoveObjects(List<ZDO> currentNearObjects,List<ZDO> distant){} }
namespace RunicStorageNetwork.API {public class ApiContext {public ZDOID ConsumerId;public string ModId;public ApiContext(ZDOID id,string mod){ConsumerId=id;ModId=mod;}}}
namespace RunicStorageNetwork {
 class InventoryDelta {}
 class UnloadedReplica:Component {}
 static class R {
  internal static string Key(ZDOID id)=>id.User+":"+id.Id;internal static bool Valid(ZNetView view)=>view&&view.Data!=null;internal static ZNetView View(Container c)=>c?.View;
  internal static T Get<T>(object o,string k)=>(T)o.GetType().GetField(k).GetValue(o);
  internal static void Set(object o,string k,object v)=>o.GetType().GetField(k).SetValue(o,v);
  internal static object Call(object o,string m,Type[] args,params object[] values)=>o.GetType().GetMethod(m,args).Invoke(o,values);
  internal static object Call(object o,string m)=>o.GetType().GetMethod(m).Invoke(o,null);
 }
 class Transport {
  internal static Transport Instance=new Transport();internal static long Server=7;internal static int InternalMutation;
  internal SourceGate SharedSourceGate=new SourceGate();internal static Func<Inventory,bool> ExtraInventoryLock;internal static Func<ItemDrop.ItemData,bool> ExtraItemLock;internal static Func<ZDO,long,bool?> ExtraOwnerRule;
  internal static bool Locked(Inventory inv)=>InternalMutation==0&&(ExtraInventoryLock?.Invoke(inv)??false);internal static bool Reserved(ZDO z)=>z.GetString("rsn_lease","")!=""||Instance.SharedSourceGate.Held(R.Key(z.m_uid));
 }
 static class Integrations {internal static readonly HashSet<Inventory> Blocked=new HashSet<Inventory>();internal static void Block(Inventory i,bool v){if(v)Blocked.Add(i);else Blocked.Remove(i);}internal static bool IsBusy(Inventory i)=>Blocked.Contains(i);}
 static class ApiWorld {internal static bool Allowed=true;internal static bool Eligible(ZDO z,long creator)=>Allowed;internal static Inventory Decode(ZDO z,byte[] b)=>new Inventory{Bytes=b};internal static void Invalidate(ZDOID id){} }
 static class UnloadedNetworks {internal static Container ApiContainer(ZDO z,out bool created){created=false;if(!ZNetScene.instance.Live.TryGetValue(z.m_uid,out var c)){created=true;c=new Container{View=new ZNetView{Data=z}};ZNetScene.instance.Live[z.m_uid]=c;}return c;}internal static void ApiReleaseAdapter(ZDOID id){} }
 static class RemoteContext {internal static ZDO Data(ZDOID id)=>ZDOMan.instance.GetZDO(id);}
 static class Stockroom {internal static List<Stock> Snapshot(Inventory i,string key,object ignored,bool chest)=>new List<Stock>{new Stock(key,"Wood",1,10)};}
 static class StorageIndex {internal static void Changed(string key){} }
 static class ApiRuntime {
  internal static bool Enabled=true;internal static readonly List<(long Peer,string Name,ZPackage Data)> Sent=new List<(long,string,ZPackage)>();
  internal static void Send(long peer,string name,ZPackage p)=>Sent.Add((peer,name,p));internal static bool HasPeer(long peer)=>true;internal static void Diagnostic(string category,string message){}
 }
}
static class ApiOwnershipRuntimeTests {
 static int passed;static void Check(bool b,string s){if(!b)throw new Exception(s);}
 static void Reset(){ApiOwnership.Clear();ZNet.UID=7;ZNet.instance.Server=true;ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();Transport.Instance=new Transport();ApiRuntime.Sent.Clear();Integrations.Blocked.Clear();ApiWorld.Allowed=true;ApiRuntime.Enabled=true;ApiOwnership.Install(new HarmonyLib.Harmony());}
 static void Test(string name,Action action){Reset();action();passed++;Console.WriteLine("PASS API ownership "+name);}
 static ZDO Source(uint id=1,long owner=7,bool fence=false){var z=new ZDO{m_uid=new ZDOID(7,id),Owner=owner,OwnerRevision=5};z.Set(ZDOVars.s_items,new byte[]{1,2,3});if(fence)z.Set("rsn_api_fence",1);ZDOMan.instance.Data[z.m_uid]=z;return z;}
 static ZPackage Frame(params (ZDO Z,ushort Epoch,uint Version)[] rows){var p=new ZPackage();p.Write(1);p.Write(new ZDOID(99,99));foreach(var r in rows){p.Write(r.Z.m_uid);p.Write(r.Epoch);p.Write(r.Version);p.Write(r.Z.Owner);p.Write(default(Vector3));p.Write(new byte[]{6,5,4});}p.Write(ZDOID.None);return p;}
 static ZPackage Filter(long sender,ZPackage p,out bool ok){object[] args={ZDOMan.instance,new ZRpc{Peer=sender},p};ok=(bool)typeof(ApiOwnership).GetMethod("FilterReplication",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);return (ZPackage)args[2];}
 public static int Main(){try{
  Test("unrelated vanilla packet remains byte-identical",()=>{var a=Source();var p=Frame((a,5,99));var result=Filter(17,p,out bool ok);Check(ok&&ReferenceEquals(p,result),"unrelated copied or changed");});
  Test("old owner higher data revision removed, unrelated record preserved",()=>{var a=Source(fence:true);var b=Source(2,17);var actual=Filter(17,Frame((a,4,100000),(b,5,101)),out bool ok);Check(ok&&actual.GetArray().SequenceEqual(Frame((b,5,101)).GetArray()),"stale frame survived or unrelated lost");});
  Test("current owner frame allowed after release",()=>{var a=Source(owner:17,fence:true);var p=Frame((a,5,88));Check(ReferenceEquals(p,Filter(17,p,out var ok))&&ok,"owner blocked");});
  Test("invalidations and terminator preserved when all frames dropped",()=>{var a=Source(fence:true);var result=Filter(17,Frame((a,4,999)),out bool ok);Check(ok&&result.GetArray().SequenceEqual(Frame().GetArray()),"framing damaged");});
  Test("malformed nested frame cannot bypass guard",()=>{var a=Source(fence:true);var p=Frame((a,4,100));byte[] bytes=p.GetArray().Take(p.Size()-14).ToArray();Filter(17,new ZPackage(bytes),out bool ok);Check(!ok,"malformed allowed");});
  Test("server claim owns and locks same source",()=>{var z=Source();Transport.Instance.SharedSourceGate.TryAcquire("op",new[]{R.Key(z.m_uid)});var s=ApiOwnership.Prepare(z,"op",1,0);var inventory=s.Container.GetInventory();Check(s.Ready&&!s.Denied&&Transport.Locked(inventory)&&z.GetString("rsn_lease","")=="op","claim failed");Check(ApiOwnership.Release(s)&&!Transport.Locked(inventory)&&z.GetString("rsn_lease","")==""&&!Transport.Instance.SharedSourceGate.Held(R.Key(z.m_uid)),"release leak");Check(s.Container==null&&s.Before==null&&s.Delta==null,"retained inventory after completion");});
  Test("same request reuses prepared source",()=>{var z=Source();var a=ApiOwnership.Prepare(z,"op",1,0);Check(ReferenceEquals(a,ApiOwnership.Prepare(z,"op",1,1))&&ApiOwnership.Prepare(z,"other",1,1)==null,"duplicate source");});
  Test("cleanup cannot clear a different reservation",()=>{var z=Source();var s=ApiOwnership.Prepare(z,"op",1,0);z.Set("rsn_lease","foreign");Check(!ApiOwnership.Release(s)&&z.GetString("rsn_lease","")=="foreign"&&Transport.Locked(s.Container.Inventory),"foreign reservation cleared");z.Set("rsn_lease","op");Check(ApiOwnership.Release(s),"original cleanup refused");});
  Test("held source rejects even current-owner packet",()=>{var z=Source();ApiOwnership.Prepare(z,"op",1,0);var p=Filter(7,Frame((z,z.OwnerRevision,9999)),out bool ok);Check(ok&&p.GetArray().SequenceEqual(Frame().GetArray()),"held packet accepted");});
  Test("denied access causes no handoff or debit",()=>{var z=Source(owner:0);var old=z.GetByteArray(ZDOVars.s_items);ApiWorld.Allowed=false;var s=ApiOwnership.Prepare(z,"op",1,0);Check(s.Denied&&!s.Ready&&z.Owner==0&&old.SequenceEqual(z.GetByteArray(ZDOVars.s_items)),"access debit");Check(ApiOwnership.Release(s),"unpaid release");});
  Test("remote source waits for actual handoff",()=>{var z=Source(owner:17);var s=ApiOwnership.Prepare(z,"op",1,0);Check(!s.Ready&&!s.Denied&&z.Owner==17&&ApiRuntime.Sent.Single().Name=="prepare","stole owner");});
  Test("forged grant cannot complete ownership",()=>{var z=Source(owner:17);var s=ApiOwnership.Prepare(z,"op",1,0);var p=new ZPackage();p.Write("op");p.Write(z.m_uid);p.Write(true);p.SetPos(0);ApiOwnership.ReceiveGrant(18,p);Check(!s.Ready&&z.Owner==17,"forged grant");});
  Test("fragmented grant waits for all bytes and tolerates duplicate chunk",()=>{var z=Source(owner:17);var s=ApiOwnership.Prepare(z,"op",1,0);Action<int,byte[]> grant=(offset,part)=>{var p=new ZPackage();p.Write("op");p.Write(z.m_uid);p.Write(true);p.Write(5);p.Write((uint)10);p.Write(4);p.Write(offset);p.Write(part);p.SetPos(0);ApiOwnership.ReceiveGrant(17,p);};grant(0,new byte[]{1,2});Check(!s.Ready,"partial handoff");grant(0,new byte[]{1,2});grant(2,new byte[]{3,4});Check(s.Ready&&z.Owner==7&&z.GetByteArray(ZDOVars.s_items).SequenceEqual(new byte[]{1,2,3,4}),"grant replay");});
  Test("client offer does not unlock on timer",()=>{var z=Source(owner:17);var c=new Container{View=new ZNetView{Data=z}};ZNetScene.instance.Live[z.m_uid]=c;ZNet.UID=17;ZNet.instance.Server=false;var p=new ZPackage();p.Write("op");p.Write(z.m_uid);p.Write(5);p.Write((long)1);p.SetPos(0);ApiOwnership.ReceivePrepare(7,p);Check(Transport.Locked(c.Inventory),"offer not blocked");ApiOwnership.Tick(1000);Check(Transport.Locked(c.Inventory)&&ApiRuntime.Sent.Any(x=>x.Name=="ticket"),"blind timeout unlock");var release=new ZPackage();release.Write("op");release.Write(z.m_uid);release.SetPos(0);ApiOwnership.ReceiveRelease(7,release);Check(!Transport.Locked(c.Inventory)&&z.GetString("rsn_lease","")=="","release ignored");});
  Test("ownership revision wrap fails before touching contents",()=>{var z=Source();z.OwnerRevision=ushort.MaxValue;Check(ApiOwnership.Prepare(z,"op",1,0)==null&&z.GetString("rsn_lease","")=="","wrapped owner fence");});
  Console.WriteLine("API ownership runtime tests: "+passed+" passed (game stand-ins)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
