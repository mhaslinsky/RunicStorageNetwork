using System;
using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace RunicStorageNetwork {
 // Keep every prefab registered so disabling content cannot erase saved objects.
 // Server-synchronized switches control recipes and the capability checks instead.
 internal static class ContentSettings {
  static ConfigEntry<bool> terminal,builder,gateway;
  static GameObject terminalPrefab,gatewayPrefab;
  internal static bool TerminalEnabled=>terminal==null||terminal.Value;
  internal static bool BuilderEnabled=>builder==null||builder.Value;
  internal static bool GatewayEnabled=>gateway==null||gateway.Value;
  internal static bool GatewayRecipeEnabled=>GatewayEnabled&&UnloadedNetworks.Installed;
  internal static void Bind(ConfigFile config){
   terminal=Option(config,"StorageCodexEnabled","Storage Codex terminal: building recipe and storage window. / Кодекс запасов: рецепт постройки и окно хранилища.");
   builder=Option(config,"BuildersCodexEnabled","Equippable Builder's Codex: crafting recipe, binding and network building bonus. / Экипируемый Кодекс строителя: рецепт, привязка и снабжение для строительства.");
   gateway=Option(config,"RunicGatewayEnabled","Runic Gateway: building recipe and network links. Also requires ExperimentalUnloadedNetworks = true and a restart. / Рунический мост: рецепт и связи сети. Дополнительно требует ExperimentalUnloadedNetworks = true и перезапуска.");
   foreach(var option in new[]{terminal,builder,gateway})option.SettingChanged+=Changed;
   ItemManager.OnItemsRegistered+=ApplyRecipes;PieceManager.OnPiecesRegistered+=ApplyRecipes;
  }
  static ConfigEntry<bool> Option(ConfigFile config,string name,string description)=>config.Bind("Content",name,true,new ConfigDescription(description+" Available only when true. Default: true. Existing objects are kept when disabled. / Доступно только при true. По умолчанию: true. Созданные объекты сохраняются при отключении.",null,new ConfigurationManagerAttributes{IsAdminOnly=true}));
  internal static void Attach(GameObject terminalModel,GameObject gatewayModel){terminalPrefab=terminalModel;gatewayPrefab=gatewayModel;ApplyRecipes();}
  internal static void ApplyRecipes(){
   if(terminalPrefab)terminalPrefab.GetComponent<Piece>().m_enabled=TerminalEnabled;
   if(gatewayPrefab)gatewayPrefab.GetComponent<Piece>().m_enabled=GatewayRecipeEnabled;
   if(ObjectDB.instance)foreach(var recipe in ObjectDB.instance.m_recipes)
    if(recipe&&recipe.m_item&&recipe.m_item.gameObject.name==BuilderCodexItem.PrefabName)recipe.m_enabled=BuilderEnabled;
  }
  internal static bool AllowsPiece(Piece piece){
   if(!piece)return true;
   string name=R.Id(piece.gameObject);
   return name==TerminalPiece.PrefabName?TerminalEnabled:name!=Gateway.PrefabName||GatewayRecipeEnabled&&UnloadedNetworks.Enabled;
  }
  internal static bool AllowsRecipe(Recipe recipe)=>!recipe||!recipe.m_item||recipe.m_item.gameObject.name!=BuilderCodexItem.PrefabName||BuilderEnabled;
  static void Changed(object sender,EventArgs args){
   ApplyRecipes();GatewayRuntime.Clear();UnloadedNetworks.SettingsChanged();Topology.Dirty();
   if(!TerminalEnabled)NetworkTerminal.Close();
   if(Player.m_localPlayer){
    BuilderCodex.EquipmentChanged(Player.m_localPlayer);
    R.Call(Player.m_localPlayer,"UpdateKnownRecipesList",Type.EmptyTypes);
    R.Call(Player.m_localPlayer,"UpdateAvailablePiecesList",Type.EmptyTypes);
   }
  }
  internal static void Unbind(){
   foreach(var option in new[]{terminal,builder,gateway})if(option!=null)option.SettingChanged-=Changed;
   ItemManager.OnItemsRegistered-=ApplyRecipes;PieceManager.OnPiecesRegistered-=ApplyRecipes;
   terminalPrefab=gatewayPrefab=null;
  }
 }
}
