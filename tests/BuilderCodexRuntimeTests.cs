#if BUILDER_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork;
using RunicStorageNetwork.Logic;
using UnityEngine;
namespace UnityEngine {
 public class Object {public string name;public static implicit operator bool(Object o)=>o!=null;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt(Math.Pow(a.x-b.x,2)+Math.Pow(a.y-b.y,2)+Math.Pow(a.z-b.z,2));}
 public class Transform {public Vector3 position;}
 public class Component:Object {public Transform transform=new Transform();internal ZNetView View=new ZNetView();public virtual T GetComponent<T>() where T:class=>View as T;}
 public class GameObject:Object {}
}
struct ZDOID {internal string Id;public static bool operator ==(ZDOID a,ZDOID b)=>a.Id==b.Id;public static bool operator !=(ZDOID a,ZDOID b)=>a.Id!=b.Id;public override bool Equals(object o)=>o is ZDOID id&&id==this;public override int GetHashCode()=>Id?.GetHashCode()??0;}
static class Hashes {public static int GetStableHashCode(this string s){int h=17;unchecked{foreach(char c in s)h=h*31+c;}return h;}}
static class ZDOVars {public const int s_utilityItem=1;}
class ZDO {public ZDOID m_uid;public int Utility;internal int Writes;internal Dictionary<string,string> Data=new Dictionary<string,string>();public int GetInt(int key,int fallback)=>Utility;public string GetString(string key,string fallback)=>Data.TryGetValue(key,out var v)?v:fallback;public void Set(string key,string value){Writes++;Data[key]=value;}}
class ZNetView:UnityEngine.Object {internal ZDO Data=new ZDO();internal bool Own=true;public ZDO GetZDO()=>Data;public bool IsOwner()=>Own;}
class ZDOMan {public static ZDOMan instance=new ZDOMan();internal int Sends;public void ForceSendZDO(ZDOID id){Sends++;}}
class ItemDrop {internal class ItemData {public GameObject m_dropPrefab;public bool m_equipped;public Dictionary<string,string> m_customData=new Dictionary<string,string>();}}
class Inventory {internal List<ItemDrop.ItemData> Items=new List<ItemDrop.ItemData>();internal int Changes;public bool ContainsItem(ItemDrop.ItemData item)=>Items.Contains(item);}
class Humanoid:Component {}
class Player:Humanoid {
 public static Player m_localPlayer;public ItemDrop.ItemData m_utilityItem;internal Inventory Inventory=new Inventory();internal CraftingStation Station;internal string LastMessage;internal bool Building;
 public Inventory GetInventory()=>Inventory;public long GetPlayerID()=>1;public ZDOID GetZDOID()=>View.Data.m_uid;public bool InPlaceMode()=>Building;public CraftingStation GetCurrentCraftingStation()=>Station;
 public void Message(MessageHud.MessageType kind,string text){LastMessage=text;}
}
class MessageHud {public enum MessageType {TopLeft}}
class CraftingStation:Component {internal bool m_upgrader;}
class PieceTable:UnityEngine.Object {}
namespace RunicStorageNetwork {
 static class BuilderCodexItem {internal const string PrefabName="RSN_RunicBuilderCodex";}
 class NetworkMember:UnityEngine.Object {internal string SavedNetwork;}
 class Core:Component {internal bool Valid=true;internal string Label="";internal NetworkMember Member=new NetworkMember();public override T GetComponent<T>()=>Member as T??base.GetComponent<T>();internal static Vector3 OrdinaryPoint;internal static Core Choose(Vector3 point,long actor){OrdinaryPoint=point;return Topology.Root;}}
 static class R {
  internal static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field).GetValue(o);
  internal static ZNetView View(Component c)=>c?.View;internal static bool Valid(ZNetView v)=>v&&v.Data!=null;
  internal static object Call(object o,string method,Type[] types,params object[] args){((Inventory)o).Changes++;return null;}
 }
 static class Topology {
  internal static NetworkGraph Graph=new NetworkGraph(Array.Empty<NetworkNode>(),50);internal static Core Root;internal static int Refreshes,Selections;internal static string SelectedBinding;
  internal static void Refresh(){Refreshes++;}internal static Core LabelRootSnapshot(string network)=>Root!=null&&Graph.Nodes.Values.Any(n=>n.Network==network)?Root:null;
  internal static Core Choose(Vector3 point,long actor,string binding=null){Selections++;SelectedBinding=binding;return Root;}
 }
 static class NetworkName {internal static string For(NetworkMember member)=>Topology.Root.Label;internal static string Read(Core core)=>core.Label;}
 static class Access {internal static bool Allowed=true;internal static bool Ward(Vector3 point,long actor)=>Allowed;}
 static class Transport {internal static bool Busy,ItemBusy;internal static bool Locked(Inventory inv)=>Busy;internal static bool LockedItem(ItemDrop.ItemData item)=>ItemBusy;}
 static partial class Actions {internal static bool Busy,Tool=true;internal static bool Locked(Inventory inv)=>Busy;internal static PieceTable BuildTable(Player p)=>Tool?new PieceTable():null;}
 static class Plugin {internal class Number {public float Value=50;}internal static Number RelayLink=new Number();internal static bool Enabled=true;}
 static class RsnLocalization {internal static string Text(string key,params object[] args)=>string.Format(TranslationCatalog.Get("English","rsn_"+key),args);}
 class Operation {internal bool Build;internal ZDOID Actor;}
 static class RemoteContext {internal static ZDO Data(ZDOID id)=>Player.m_localPlayer?.View.Data;}
}
static class BuilderCodexRuntimeTests {
 static Player player;static Core core;static ItemDrop.ItemData book;static int passed;
 static ItemDrop.ItemData Book(string binding=null){var b=new ItemDrop.ItemData{m_dropPrefab=new GameObject{name=BuilderCodexItem.PrefabName}};if(binding!=null)b.m_customData[BuilderCodex.BindingKey]=binding;player.Inventory.Items.Add(b);return b;}
 static void Equip(ItemDrop.ItemData item){if(player.m_utilityItem!=null)player.m_utilityItem.m_equipped=false;player.m_utilityItem=item;if(item!=null)item.m_equipped=true;player.View.Data.Utility=item?.m_dropPrefab.name.GetStableHashCode()??0;BuilderCodex.EquipmentChanged(player);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Test(string name,Action body){
  player=new Player();player.View.Data.m_uid=new ZDOID{Id="actor"};Player.m_localPlayer=player;core=new Core{Label="Workshop"};core.Member.SavedNetwork="saved-A";Topology.Root=core;
  Topology.Graph=NetworkGraph.Automatic(new[]{new NetworkNode{Id="new-core-id",Network="saved-A",Root=true,Confirmed=true,Storage=20,Supply=20}},50);
  Topology.Refreshes=Topology.Selections=0;Topology.SelectedBinding=null;Access.Allowed=Plugin.Enabled=Actions.Tool=true;Actions.Busy=Transport.Busy=Transport.ItemBusy=false;ZDOMan.instance=new ZDOMan();book=Book();body();passed++;Console.WriteLine("PASS builder runtime "+name);
 }
 public static int Main(){try{
  Test("using book stores persistent core identity and display name on that item",()=>{Check(BuilderCodex.Use(core,player,book),"use not handled");Check(book.m_customData[BuilderCodex.BindingKey]=="saved-A"&&book.m_customData[BuilderCodex.NameKey]=="Workshop"&&player.Inventory.Changes==1,"binding not saved on item");Check(!book.m_equipped&&player.Inventory.ContainsItem(book),"binding consumed/equipped book");});
  Test("using a different core rebinds only this copy",()=>{var other=Book("saved-A");BuilderCodex.Use(core,player,book);core.Member.SavedNetwork="saved-B";core.Label="Harbour";BuilderCodex.Use(core,player,book);Check(book.m_customData[BuilderCodex.BindingKey]=="saved-B"&&other.m_customData[BuilderCodex.BindingKey]=="saved-A","copies share binding");});
  Test("ordinary crafting codex is not intercepted",()=>{book.m_dropPrefab.name="RSN_RunicCodex";Check(!BuilderCodex.Use(core,player,book)&&book.m_customData.Count==0,"ordinary ingredient book bound");});
  Test("ward blocks binding",()=>{Access.Allowed=false;Check(BuilderCodex.Use(core,player,book)&&book.m_customData.Count==0,"ward bypass");});
  Test("distant or absent inventory book cannot bind",()=>{player.transform.position=new Vector3(11,0,0);BuilderCodex.Use(core,player,book);Check(book.m_customData.Count==0,"distance bypass");player.transform.position=new Vector3();player.Inventory.Items.Clear();BuilderCodex.Use(core,player,book);Check(book.m_customData.Count==0,"foreign book modified");});
  Test("pending transaction prevents rebinding",()=>{Actions.Busy=true;BuilderCodex.Use(core,player,book);Check(book.m_customData.Count==0,"reserved binding changed");Actions.Busy=false;Transport.ItemBusy=true;BuilderCodex.Use(core,player,book);Check(book.m_customData.Count==0,"item reservation ignored");});
  Test("carried book gives no capability; equipped unbound book stays unbound",()=>{Check(BuilderCodex.Binding(player)==null,"carried book equipped");Equip(book);Check(BuilderCodex.Binding(player)==""&&BuilderCodex.RemoteBinding(player.View.Data)=="","empty binding silently auto-assigned");});
  Test("equip and unequip synchronize binding with native utility hash",()=>{BuilderCodex.Use(core,player,book);Equip(book);Check(BuilderCodex.RemoteBinding(player.View.Data)=="saved-A","remote equip binding missing");Equip(null);Check(BuilderCodex.Binding(player)==null&&BuilderCodex.RemoteBinding(player.View.Data)==null&&player.View.Data.GetString(BuilderCodex.BindingKey,"")=="","unequipped capability retained");});
  Test("rebind while equipped updates remote capability",()=>{Equip(book);BuilderCodex.Use(core,player,book);Check(BuilderCodex.RemoteBinding(player.View.Data)=="saved-A","bind did not synchronize");core.Member.SavedNetwork="saved-B";BuilderCodex.Use(core,player,book);Check(BuilderCodex.RemoteBinding(player.View.Data)=="saved-B","old remote binding retained");});
  Test("unchanged recipe checks never resend binding or rebuild topology",()=>{BuilderCodex.Use(core,player,book);Equip(book);int sends=ZDOMan.instance.Sends,refreshes=Topology.Refreshes;for(int i=0;i<1000;i++)BuilderCodex.Binding(player);Check(sends==ZDOMan.instance.Sends&&refreshes==Topology.Refreshes,"binding poll has repeated side effects");});
  Test("native equipped hash is mandatory even with stale binding data",()=>{player.View.Data.Set(BuilderCodex.BindingKey,"saved-A");player.View.Data.Utility="BeltStrength".GetStableHashCode();Check(BuilderCodex.RemoteBinding(player.View.Data)==null,"stale ZDO binding grants range to ordinary belt");});
  Test("building passes binding but workbench crafting keeps ordinary selection",()=>{BuilderCodex.Use(core,player,book);Equip(book);Actions.Context(player,false);Check(Topology.SelectedBinding=="saved-A","build ignored binding");int selections=Topology.Selections;player.Station=new CraftingStation();player.Station.transform.position=new Vector3(3,0,0);Actions.Context(player,true);Check(Topology.Selections==selections&&Core.OrdinaryPoint.x==3,"craft used wearable network");});
  Test("unsupported build tool cannot use portable supply",()=>{Equip(book);Actions.Tool=false;Check(Actions.Context(player,false)==null&&Topology.Selections==0,"tool policy bypass");});
  Test("tooltip follows current name without changing identity or waking network",()=>{BuilderCodex.Use(core,player,book);core.Label="Renamed";int before=Topology.Refreshes;string text=BuilderCodex.Tooltip(book);Check(text.Contains("Renamed")&&book.m_customData[BuilderCodex.BindingKey]=="saved-A"&&before==Topology.Refreshes,"name stale, identity renamed or tooltip refresh");});
  Test("unloaded tooltip falls back to saved sanitized name",()=>{BuilderCodex.Use(core,player,book);Topology.Root=null;book.m_customData[BuilderCodex.NameKey]="<b>Workshop</b>\n$foo";string text=BuilderCodex.Tooltip(book);Check(text.Contains("Workshop")&&!text.Contains("<b>")&&!text.Contains("$foo"),"unsafe/lost saved name");});
  Test("unnamed core can be bound and described",()=>{core.Label="";BuilderCodex.Use(core,player,book);Check(BuilderCodex.Tooltip(book).Contains("Unnamed network"),"unnamed network requires naming");});
  Console.WriteLine("Builder runtime tests: "+passed+" passed (game stand-ins)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
