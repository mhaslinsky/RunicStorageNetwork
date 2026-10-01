#if CONTENT_RUNTIME_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RunicStorageNetwork;
using UnityEngine;
namespace UnityEngine {
 public class Object {public string name;public static implicit operator bool(Object o)=>o!=null;}
 public class GameObject:Object {internal Piece Piece;public T GetComponent<T>() where T:class=>Piece as T;}
}
class Piece:UnityEngine.Object {public GameObject gameObject;public bool m_enabled=true;}
class ItemDrop:UnityEngine.Object {public GameObject gameObject;}
class Recipe:UnityEngine.Object {public bool m_enabled=true;public ItemDrop m_item;}
class ObjectDB:UnityEngine.Object {public static ObjectDB instance;public List<Recipe> m_recipes=new List<Recipe>();}
class Player:UnityEngine.Object {public static Player m_localPlayer;}
namespace BepInEx.Configuration {
 public class ConfigDescription {public object[] Tags;public ConfigDescription(string text,object range,params object[] tags){Tags=tags;}}
 public class ConfigEntry<T> {T value;public ConfigDescription Description;public event EventHandler SettingChanged;public T Value {get=>value;set{this.value=value;SettingChanged?.Invoke(this,EventArgs.Empty);}}}
 public class ConfigFile {
  public Dictionary<string,ConfigEntry<bool>> Entries=new Dictionary<string,ConfigEntry<bool>>();
  public ConfigEntry<bool> Bind(string section,string name,bool initial,ConfigDescription description){if(!Entries.TryGetValue(section+"/"+name,out var e))Entries.Add(section+"/"+name,e=new ConfigEntry<bool>{Value=initial});e.Description=description;return e;}
 }
}
namespace Jotunn.Utils {public class ConfigurationManagerAttributes {public bool IsAdminOnly;}}
namespace Jotunn.Managers {
 public static class ItemManager {public static event Action OnItemsRegistered;public static void Registered()=>OnItemsRegistered?.Invoke();}
 public static class PieceManager {public static event Action OnPiecesRegistered;public static void Registered()=>OnPiecesRegistered?.Invoke();}
}
namespace RunicStorageNetwork {
 static class BuilderCodexItem {internal const string PrefabName="RSN_RunicBuilderCodex";}
 static class TerminalPiece {internal const string PrefabName="RSN_RunicStorageTerminal";}
 static class Gateway {internal const string PrefabName="RSN_RunicGateway";}
 static class UnloadedNetworks {internal static bool Installed,Enabled;internal static void SettingsChanged(){}}
 static class GatewayRuntime {internal static void Clear(){}}
 static class Topology {internal static void Dirty(){}}
 static class NetworkTerminal {internal static int Closes;internal static void Close(){Closes++;}}
 static class BuilderCodex {internal static void EquipmentChanged(Player p){}}
 static class R {internal static string Id(GameObject p)=>p.name.Replace("(Clone)","");internal static object Call(object o,string method,Type[] args)=>null;}
}
static class ContentRuntimeTests {
 static int passed;static ConfigFile config;static GameObject terminal,gateway,core;static Recipe book,ingredient;
 static GameObject Piece(string name){var p=new GameObject{name=name};p.Piece=new Piece{gameObject=p};return p;}
 static Recipe Recipe(string name)=>new Recipe{m_item=new ItemDrop{gameObject=new GameObject{name=name}}};
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Set(string name,bool value)=>config.Entries["Content/"+name].Value=value;
 static void Test(string name,Action body){
  ContentSettings.Unbind();config=new ConfigFile();ObjectDB.instance=new ObjectDB();UnloadedNetworks.Enabled=UnloadedNetworks.Installed=true;NetworkTerminal.Closes=0;
  terminal=Piece(TerminalPiece.PrefabName);gateway=Piece(Gateway.PrefabName);core=Piece("RSN_NetworkCore");
  book=Recipe(BuilderCodexItem.PrefabName);ingredient=Recipe("RSN_RunicCodex");ObjectDB.instance.m_recipes.AddRange(new[]{book,ingredient});
  ContentSettings.Bind(config);ContentSettings.Attach(terminal,gateway);body();passed++;Console.WriteLine("PASS content runtime "+name);
 }
 public static int Main(){try{
  Test("all three switches default on and are administrator-only",()=>{Check(config.Entries.Count==3&&config.Entries.Values.All(e=>e.Value&&e.Description.Tags.OfType<Jotunn.Utils.ConfigurationManagerAttributes>().Single().IsAdminOnly),"defaults/synchronization metadata");Check(terminal.Piece.m_enabled&&gateway.Piece.m_enabled&&book.m_enabled,"new content unavailable");});
  Test("independent switches cover every recipe combination without removing objects",()=>{
   for(int mask=0;mask<8;mask++){
    bool t=(mask&1)!=0,b=(mask&2)!=0,g=(mask&4)!=0;Set("StorageCodexEnabled",t);Set("BuildersCodexEnabled",b);Set("RunicGatewayEnabled",g);
    Check(terminal.Piece.m_enabled==t&&book.m_enabled==b&&gateway.Piece.m_enabled==g,"recipe settings coupled");
    Check(ContentSettings.AllowsPiece(terminal.Piece)==t&&ContentSettings.AllowsPiece(gateway.Piece)==g&&ContentSettings.AllowsRecipe(book)==b,"stale selected action bypasses switches");
    Check(core.Piece.m_enabled&&ContentSettings.AllowsPiece(core.Piece)&&ingredient.m_enabled&&ContentSettings.AllowsRecipe(ingredient)&&ObjectDB.instance.m_recipes.Count==2,"ordinary content removed or disabled");
   }
  });
  Test("experiment is required only for gateway",()=>{UnloadedNetworks.Installed=UnloadedNetworks.Enabled=false;ContentSettings.ApplyRecipes();Check(terminal.Piece.m_enabled&&book.m_enabled&&!gateway.Piece.m_enabled,"terminal or wearable depends on experiment");Check(!ContentSettings.AllowsPiece(gateway.Piece)&&ContentSettings.AllowsPiece(terminal.Piece),"disabled experiment allows building bridge");});
  Test("server declining distant storage blocks stale bridge placement",()=>{UnloadedNetworks.Enabled=false;Check(!ContentSettings.AllowsPiece(gateway.Piece),"client can build bridge without server support");});
  Test("late item registration respects saved disabled setting",()=>{Set("BuildersCodexEnabled",false);book.m_enabled=true;Jotunn.Managers.ItemManager.Registered();Check(!book.m_enabled,"late recipe registration enables disabled book");});
  Test("late piece registration and reenable preserve prefab identity",()=>{Set("StorageCodexEnabled",false);terminal.Piece.m_enabled=true;Jotunn.Managers.PieceManager.Registered();Check(!terminal.Piece.m_enabled,"late piece registration enables disabled terminal");Set("StorageCodexEnabled",true);Check(terminal.Piece.m_enabled&&terminal.name==TerminalPiece.PrefabName&&NetworkTerminal.Closes>0,"terminal lost or window not closed");});
  Test("existing false config is retained when binding default true",()=>{ContentSettings.Unbind();Set("RunicGatewayEnabled",false);ContentSettings.Bind(config);ContentSettings.Attach(terminal,gateway);Check(!ContentSettings.GatewayEnabled&&!gateway.Piece.m_enabled,"saved opt-out overwritten");});
  ContentSettings.Unbind();Console.WriteLine("Content runtime tests: "+passed+" passed (game stand-ins)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
