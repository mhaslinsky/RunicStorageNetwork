using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RunicStorageNetwork.Logic {
 internal static class TerminalGrid {
  internal const int Columns=7,VisibleRows=4,Cell=84,Gap=8,Padding=4,Pitch=Cell+Gap;
  internal const int Width=Columns*Pitch-Gap+2*Padding,Viewport=VisibleRows*Pitch-Gap+2*Padding;
  internal static float Height(int count)=>Math.Max(Viewport,((Math.Max(0,count)+Columns-1)/Columns)*Pitch-Gap+2*Padding);
  internal static float ClampOffset(float offset,int count)=>Math.Max(0,Math.Min(offset,Height(count)-Viewport));
  internal static int FirstIndex(float offset,int count)=>(int)(ClampOffset(offset,count)/Pitch)*Columns;
  internal static float PreserveOffset(IReadOnlyList<string> before,IReadOnlyList<string> after,float offset){
   int first=FirstIndex(offset,before.Count);
   if(first<before.Count){
    for(int i=0;i<after.Count;i++)if(after[i]==before[first])return ClampOffset((i/Columns)*Pitch+offset%Pitch,after.Count);
   }
   return ClampOffset(offset,after.Count);
  }
 }
 // Valheim builds every GUI screen as its own root canvas and scales it with GuiScaler:
 // scaleFactor=min(width/1920,height/1080)*GuiScale. Jotunn's CustomGUIFront is a root
 // canvas too but carries no GuiScaler, so its factor stays 1 and anything parented there
 // is laid out in raw screen pixels. Reproduce the game's factor on our own canvas.
 internal static class TerminalScale {
  internal const int PanelWidth=1080,PanelHeight=640,ReferenceWidth=1920,ReferenceHeight=1080;
  internal static float Factor(int width,int height,float guiScale){
   if(width<=0||height<=0)return 1;
   float game=Math.Min(width/(float)ReferenceWidth,height/(float)ReferenceHeight)*Math.Max(.1f,guiScale);
   // Never grow past what the game asks for; shrink below it only to keep the panel on screen.
   return Math.Max(.05f,Math.Min(game,Math.Min(width/(float)PanelWidth,height/(float)PanelHeight)));
  }
 }
 internal static class TerminalRules {
  internal const int MaxAmount=10000,MaxParcels=256;
  internal static bool Quantity(string text,int available,out int value)=>int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value)&&value>0&&value<=Math.Min(MaxAmount,available);
  internal static bool Search(string name,string query)=>string.IsNullOrWhiteSpace(query)||CultureInfo.CurrentCulture.CompareInfo.IndexOf(name,query.Trim(),CompareOptions.IgnoreCase)>=0;
  internal static bool Matches(IEnumerable<Stock> actual,IEnumerable<Debit> expected){
   var values=actual.ToList();var plan=expected.ToList();
   if(values.Any(s=>s.Amount<=0)||plan.Any(d=>d.Amount<=0||d.Source=="player"))return false;
   var a=values.GroupBy(s=>(s.Source,s.Item,s.Quality)).ToDictionary(g=>g.Key,g=>g.Sum(s=>(long)s.Amount));
   var b=plan.GroupBy(s=>(s.Source,s.Item,s.Quality)).ToDictionary(g=>g.Key,g=>g.Sum(s=>(long)s.Amount));
   return a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out long count)&&count==p.Value);
  }
 }
 // Plan before touching the inventory. Keys include the complete serialized item
 // metadata (except count/position), so unlike ordinary stacking no data is lost.
 internal static class DeliveryPlanner {
  internal sealed class Stack {internal string Key;internal int Amount,Max;internal Stack(string key,int amount,int max){Key=key;Amount=amount;Max=max;}}
  internal sealed class Step {internal int Parcel,Slot,Amount;}
  internal static List<Step> Plan(Stack[] slots,IReadOnlyList<Stack> parcels){
   var state=slots.Select(s=>s==null?null:new Stack(s.Key,s.Amount,s.Max)).ToArray();var plan=new List<Step>();
   for(int i=0;i<parcels.Count;i++){
    var p=parcels[i];if(p==null||p.Amount<=0||p.Max<1||p.Amount>p.Max)return null;int left=p.Amount;
    for(int k=0;k<state.Length&&left>0;k++){
     var s=state[k];if(s==null||s.Key!=p.Key||s.Max!=p.Max)continue;
     int count=Math.Min(left,Math.Max(0,s.Max-s.Amount));if(count==0)continue;
     s.Amount+=count;left-=count;plan.Add(new Step{Parcel=i,Slot=k,Amount=count});
    }
    if(left==0)continue;int empty=Array.FindIndex(state,s=>s==null);if(empty<0)return null;
    state[empty]=new Stack(p.Key,left,p.Max);plan.Add(new Step{Parcel=i,Slot=empty,Amount=left});
   }
   return plan;
  }
 }
}
