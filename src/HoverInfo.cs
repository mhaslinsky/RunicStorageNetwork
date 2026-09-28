using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 // Only the currently hovered piece has a work queue. GetHoverText never scans storage
 // or requests a topology refresh; NetworkSystem services the queue once per frame.
 internal static class HoverInfo {
  static readonly IncrementalCount<Container> count=new IncrementalCount<Container>();
  static readonly DisplayAccessCache access=new DisplayAccessCache();
  static readonly Func<Container,bool> include=Include;
  static readonly Func<bool> expired=Expired;
  static NetworkMember target;static Core core;static Player player;
  static long actor,started;static int requestedFrame,revision;
  static float nextCount,nextText;static bool local;static string text;
  internal static string Text(NetworkMember node){
   var p=Player.m_localPlayer;
   if(!node||!node.Valid||!p)return Localization.instance.Localize(node is Relay?"$rsn_relay_name":"$rsn_name");
   long id=p.GetPlayerID();
   if(target!=node||player!=p||actor!=id){
    Clear();target=node;player=p;actor=id;local=node is Relay;revision=Topology.DisplayRevision;Render();
   }
   requestedFrame=Time.frameCount;
   return text;
  }
  internal static void Clear(){target=null;core=null;player=null;count.Reset();access.Clear();text=null;nextCount=nextText=0;}
  internal static void Tick(){
   if(!target||!target.Valid||!player||player!=Player.m_localPlayer||requestedFrame<Time.frameCount-1){Clear();return;}
   try {
    if(UnloadedNetworks.Enabled){Topology.Refresh();UnloadedNetworks.Request(Core.Choose(target.transform.position,actor));}
    float now=Time.unscaledTime;
    // Normal mode uses NetworkSystem's snapshot; the experiment refreshes only on demand.
    var graph=Topology.ForActor(actor);
    var root=graph.Nodes.TryGetValue(target.Id,out var node)&&graph.Roots.TryGetValue(node.Network,out var rootId)?Topology.Member(rootId)?.GetComponent<Core>():null;
    if(core!=root||revision!=Topology.DisplayRevision){core=root;revision=Topology.DisplayRevision;count.Reset();nextCount=nextText=0;}
    if(!count.Running&&now>=nextCount){count.Begin(core&&core.Valid?core.Pool.ToArray():Array.Empty<Container>());}
    if(count.Running){
     access.Clear();started=Stopwatch.GetTimestamp();
     // At most 32 checks, and stop after the first check that crosses 0.75 ms.
     // One indivisible vanilla check may itself take longer than the time budget.
     count.Step(include,32,expired);
     if(!count.Running){nextCount=now+1;nextText=0;}
    }
    if(now>=nextText)Render();
   }catch(Exception e){count.Reset();nextCount=Time.unscaledTime+1;Plugin.Error("hover display",e);}
  }
  static bool Expired()=>(Stopwatch.GetTimestamp()-started)>=Stopwatch.Frequency*.00075;
  static bool Include(Container c){
   if(!c||!target||!target.Valid||!core||!core.Valid)return false;
   if(local&&(c.transform.position-target.transform.position).sqrMagnitude>Plugin.RelayStorage.Value*Plugin.RelayStorage.Value)return false;
   return Access.Container(c,actor,core,out _,display:access,preview:true);
  }
  static void Render(){
   string amount=count.Value.HasValue?count.Value.Value.ToString():"…";
   string network=target.Network,state=Topology.State(target);
   if(local){
    text=Localization.instance.Localize("$rsn_relay_name\n"+state+"\n$rsn_containers "+amount+
     "\n$rsn_link "+Plugin.RelayLink.Value+" $rsn_metres • $rsn_storage "+Plugin.RelayStorage.Value+" $rsn_metres • $rsn_supply "+Plugin.RelaySupply.Value+" $rsn_metres");
   }else text=Localization.instance.Localize("$rsn_name\n"+state+"\n$rsn_supply "+Plugin.SupplyRadius.Value+" $rsn_metres • $rsn_storage "+Plugin.StorageRadius.Value+" $rsn_metres\n$rsn_network_containers "+amount);
   string name=NetworkName.For(target);
   if(name.Length>0)text+="\n"+RsnLocalization.Text("network")+": "+name;
   if(!local&&Access.Ward(target.transform.position,actor))text+="\n"+Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] $rsn_network_rename");
   nextText=Time.unscaledTime+.25f;
  }
 }

 // Exists only for hover counting. All caches expire before the next batch/frame;
 // transactions continue to call Access.Container without this optional context.
 internal sealed class DisplayAccessCache {
  readonly Dictionary<Vector2s,bool> zones=new Dictionary<Vector2s,bool>();
  readonly Dictionary<Vector3,bool> wards=new Dictionary<Vector3,bool>();
  readonly Dictionary<string,bool> nodes=new Dictionary<string,bool>(StringComparer.Ordinal);
  internal void Clear(){zones.Clear();wards.Clear();nodes.Clear();}
  internal bool Ready(Vector3 point){
   var zone=ZoneSystem.GetZone(point);
   if(!zones.TryGetValue(zone,out bool ready))zones[zone]=ready=ZNetScene.instance&&ZNetScene.instance.IsAreaReady(point);
   return ready;
  }
  internal bool Ward(Vector3 point,long actor){if(!wards.TryGetValue(point,out bool allowed))wards[point]=allowed=Access.Ward(point,actor);return allowed;}
  bool Allowed(NetworkNode node,long actor){
   if(!nodes.TryGetValue(node.Id,out bool allowed)){
    var member=Topology.Member(node.Id);nodes[node.Id]=allowed=member&&member.Valid&&(actor==0||Ward(member.transform.position,actor));
   }
   return allowed;
  }
  internal bool Covers(Core core,Vector3 point,long actor){
   var member=core.GetComponent<NetworkMember>();var graph=Topology.ForActor(actor);
   return member&&graph.Nodes.TryGetValue(member.Id,out var root)&&root.Confirmed&&graph.Covers(root.Network,Topology.Position(point),n=>true);
  }
 }
}
