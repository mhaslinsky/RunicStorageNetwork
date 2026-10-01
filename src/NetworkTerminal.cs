using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 // The view is shared, but every session is bound to one placed Storage Codex.
 public sealed class NetworkTerminal:MonoBehaviour {
  sealed class Entry {internal string Id,Name;internal int Quality,Count;internal ItemDrop Item;internal string Key=>Id+"/"+Quality;}
  sealed class Slot {internal GameObject Object;internal RectTransform Rect;internal Image Icon,Selection;internal Text Count,Quality;internal Button Button;internal UITooltip Tooltip;internal Entry Entry;}
  static NetworkTerminal instance;
  Core core;StorageCodex accessPoint;Player player;GameObject panel;CanvasScaler scaler;Canvas canvas;float guiScale;bool blocked;
  InputField search,quantity;Text heading,detail,available,carried,status,empty,qualityLabel;Image selectedIcon;Button take,minus,plus,stack;
  ScrollRect scroll;RectTransform content;readonly List<Slot> slots=new List<Slot>();
  List<Entry> entries=new List<Entry>(),filtered=new List<Entry>();Entry selected;
  float nextRefresh,statusUntil;string query="",selectedKey;int visibleStart=-1;bool dirtySlots;
  static readonly Vector2 Center=new Vector2(.5f,.5f);
  static readonly Color Gold=NetworkUiStyle.Gold,Muted=new Color(.78f,.75f,.67f),Bronze=NetworkUiStyle.Bronze;
  void Awake(){instance=this;}
  internal static bool Showing(StorageCodex access,Core value)=>instance&&instance.panel&&instance.panel.activeSelf&&instance.accessPoint==access&&instance.core==value;
  internal static bool Open(StorageCodex access,Core value,Player actor){
   if(!instance||!TerminalTransfer.CanUse(access,value,actor)||TerminalTransfer.Busy||Actions.Waiting!=null||CraftPreparation.HasReservation||!GUIManager.CustomGUIFront)return false;
   Close();if(InventoryGui.IsVisible())InventoryGui.instance.Hide();
   instance.accessPoint=access;instance.core=value;instance.player=actor;
   try{instance.Create();StorageIndex.Reconcile(value);instance.Refresh();instance.RenderSlots();instance.UpdateDetail();GUIManager.BlockInput(true);instance.blocked=true;return true;}
   catch(Exception e){Plugin.Error("terminal UI",e);Close();return false;}
  }
  internal static void Close(){
   if(!instance)return;TerminalTransfer.Cancel();
   if(instance.blocked){GUIManager.BlockInput(false);instance.blocked=false;}
   if(instance.scaler){UITooltip.HideTooltip();instance.scaler.gameObject.SetActive(false);Destroy(instance.scaler.gameObject);}
   instance.panel=null;instance.scaler=null;instance.canvas=null;instance.slots.Clear();instance.entries.Clear();instance.filtered.Clear();instance.selected=null;instance.selectedKey=null;instance.core=null;instance.accessPoint=null;instance.player=null;instance.visibleStart=-1;instance.statusUntil=0;
  }
  void OnDestroy(){if(instance==this){Close();instance=null;}}
  void Update(){
   if(!panel)return;
   if(!TerminalTransfer.CanUse(accessPoint,core,player)||ZInput.GetKeyDown(KeyCode.Escape)||ZInput.GetButtonDown("JoyButtonB")){Close();return;}
   try{
    Rescale();
    if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+1;Refresh();}
    RenderSlots();UpdateDetail();
   }catch(Exception e){Plugin.Error("terminal update",e);Close();}
  }
  // The CanvasScaler owns referencePixelsPerUnit, but only applies a new factor on its own
  // Update; writing the canvas too keeps the first frame from rendering at the old size.
  void Rescale(){float factor=TerminalScale.Factor(Screen.width,Screen.height,guiScale);if(factor!=scaler.scaleFactor){scaler.scaleFactor=factor;canvas.scaleFactor=factor;}}
  static string T(string key,params object[] args)=>RsnLocalization.Text(key,args);
  Text Label(string text,Transform parent,float x,float y,float width,float height,int size=20,bool title=false){
   var go=GUIManager.Instance.CreateText(text,parent,Center,Center,new Vector2(x,y),title?GUIManager.Instance.NorseBold:GUIManager.Instance.AveriaSerif,size,title?Gold:Color.white,true,Color.black,width,height,false);
   var label=go.GetComponent<Text>();label.alignment=TextAnchor.MiddleLeft;label.supportRichText=false;label.raycastTarget=false;return label;
  }
  Button Button(string text,Transform parent,float x,float y,float width,float height,Action click){
   var go=GUIManager.Instance.CreateButton(text,parent,Center,Center,new Vector2(x,y),width,height);var button=go.GetComponent<Button>();button.onClick.AddListener(()=>click());return button;
  }
  static Image Picture(string name,Transform parent,float x,float y,float width,float height){
   var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=Center;rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);
   var image=go.GetComponent<Image>();image.raycastTarget=false;return image;
  }
  static Image Icon(Transform parent,float x,float y,float size){var image=Picture("ItemIcon",parent,x,y,size,size);image.preserveAspect=true;return image;}
  static void Line(Transform parent,float x,float y,float width,float height,Color color){Picture("Divider",parent,x,y,width,height).color=color;}
  static void Border(Transform parent,float width,float height,Color color){
   Line(parent,0,height/2,width,1,color);Line(parent,0,-height/2,width,1,color);Line(parent,-width/2,0,1,height,color);Line(parent,width/2,0,1,height,color);
  }
  void Create(){
   var ui=GUIManager.Instance;
   // The GUI scale setting cannot change while this panel is open: reaching it needs
   // Escape, which closes the terminal. Read it once and follow the resolution per frame.
   scaler=NetworkUiStyle.Screen("RSN_NetworkTerminalCanvas",2000);canvas=scaler.GetComponent<Canvas>();
   guiScale=PlatformPrefs.GetFloat("GuiScale",GuiScaler.PlatformDefaultScaling);Rescale();
   panel=NetworkUiStyle.Panel(scaler.transform,TerminalScale.PanelWidth,TerminalScale.PanelHeight);panel.name="RSN_NetworkTerminal";
   heading=Label(T("terminal_title"),panel.transform,0,265,900,48,32,true);heading.alignment=TextAnchor.MiddleCenter;heading.resizeTextForBestFit=true;heading.resizeTextMinSize=22;heading.resizeTextMaxSize=32;
   Button("×",panel.transform,502,269,36,36,Close);
   Line(panel.transform,0,229,1016,1,Bronze);Line(panel.transform,177,-22,1,458,Bronze);
   search=ui.CreateInputField(panel.transform,Center,Center,new Vector2(-190,185),InputField.ContentType.Standard,T("terminal_search"),22,TerminalGrid.Width,44).GetComponent<InputField>();search.characterLimit=80;
   // Unity's standard hierarchy avoids nested canvases inside the masked viewport.
   // Sprites come from Jotunn's native game resource set.
   var view=DefaultControls.CreateScrollView(ui.ValheimControlResources);view.name="Resources";view.transform.SetParent(panel.transform,false);
   var rect=view.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Center;rect.anchoredPosition=new Vector2(-180,-42);rect.sizeDelta=new Vector2(TerminalGrid.Width+20,TerminalGrid.Viewport);
   view.GetComponent<Image>().color=Color.clear;
   scroll=view.GetComponentInChildren<ScrollRect>(true);
   if(!scroll||!scroll.content||!scroll.viewport)throw new InvalidOperationException("Terminal scroll view is missing its ScrollRect, viewport or content");
   // Clip to the viewport rectangle, not the transparent margins of a game sprite.
   var spriteMask=scroll.viewport.GetComponent<Mask>();if(spriteMask){spriteMask.enabled=false;Destroy(spriteMask);}
   var maskImage=scroll.viewport.GetComponent<Image>();if(maskImage)maskImage.enabled=false;
   if(!scroll.viewport.GetComponent<RectMask2D>())scroll.viewport.gameObject.AddComponent<RectMask2D>();
   scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=TerminalGrid.Pitch;scroll.inertia=false;
   if(scroll.horizontalScrollbar){scroll.horizontalScrollbar.gameObject.SetActive(false);scroll.horizontalScrollbar=null;}
   scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
   scroll.viewport.anchorMin=Vector2.zero;scroll.viewport.anchorMax=Vector2.one;scroll.viewport.offsetMin=Vector2.zero;scroll.viewport.offsetMax=new Vector2(-20,0);
   if(scroll.verticalScrollbar){
    var bar=scroll.verticalScrollbar;var barRect=bar.GetComponent<RectTransform>();barRect.anchorMin=new Vector2(1,0);barRect.anchorMax=Vector2.one;barRect.pivot=Vector2.one;barRect.offsetMin=new Vector2(-12,0);barRect.offsetMax=Vector2.zero;
    var background=bar.GetComponent<Image>();if(background)background.color=new Color(.09f,.08f,.06f,1);
    bar.targetGraphic.color=new Color(.62f,.55f,.42f,1);var colors=bar.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.2f,1.15f,1,1);bar.colors=colors;
   }
   content=scroll.content;content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(TerminalGrid.Width,TerminalGrid.Viewport);
   var grid=InventoryGui.instance?InventoryGui.instance.m_playerGrid:null;
   var nativeSlot=grid&&grid.m_elementPrefab?grid.m_elementPrefab.GetComponent<InventoryElement>():null;
   // One spare row allows smooth clipping while keeping the number of objects bounded.
   for(int i=0;i<TerminalGrid.Columns*(TerminalGrid.VisibleRows+1);i++){
    var slot=new Slot();slot.Button=Button("",content,0,0,TerminalGrid.Cell,TerminalGrid.Cell,()=>Select(slot.Entry));slot.Object=slot.Button.gameObject;slot.Object.name="ResourceSlot";slot.Rect=slot.Object.GetComponent<RectTransform>();slot.Rect.anchorMin=slot.Rect.anchorMax=new Vector2(0,1);
    var background=slot.Button.image;
    // The native button's image is a hit target, not the inventory slot backdrop.
    // Own the backdrop and keep its alpha on the Image so every button state stays translucent.
    background.sprite=null;background.overrideSprite=null;background.material=null;background.type=Image.Type.Simple;background.color=new Color(.10f,.085f,.065f,.32f);
    slot.Button.targetGraphic=background;slot.Button.transition=Selectable.Transition.ColorTint;
    var colors=ColorBlock.defaultColorBlock;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.5f,1.35f,1.1f,1);colors.pressedColor=new Color(.7f,.7f,.7f,1);colors.selectedColor=Color.white;colors.disabledColor=Color.white;colors.colorMultiplier=1;colors.fadeDuration=.08f;slot.Button.colors=colors;
    Border(slot.Object.transform,82,82,new Color(Bronze.r,Bronze.g,Bronze.b,.35f));
    slot.Icon=Icon(slot.Object.transform,0,3,64);
    slot.Count=Label("",slot.Object.transform,-3,-29,74,24,18);slot.Count.alignment=TextAnchor.MiddleRight;slot.Count.resizeTextForBestFit=true;slot.Count.resizeTextMinSize=12;slot.Count.resizeTextMaxSize=18;
    slot.Quality=Label("",slot.Object.transform,-3,28,72,22,16);slot.Quality.alignment=TextAnchor.MiddleRight;slot.Quality.color=Gold;
    slot.Selection=Picture("Selection",slot.Object.transform,0,0,80,80);slot.Selection.color=Color.clear;Border(slot.Selection.transform,80,80,Gold);slot.Selection.gameObject.SetActive(false);
    if(nativeSlot&&nativeSlot.m_tooltip&&nativeSlot.m_tooltip.m_tooltipPrefab){slot.Tooltip=slot.Object.AddComponent<UITooltip>();slot.Tooltip.m_tooltipPrefab=nativeSlot.m_tooltip.m_tooltipPrefab;}
    slots.Add(slot);
   }
   empty=Label("",panel.transform,-190,-42,560,90,22);empty.alignment=TextAnchor.MiddleCenter;empty.color=Muted;
   selectedIcon=Icon(panel.transform,345,136,104);selectedIcon.enabled=false;
   detail=Label(T("terminal_select"),panel.transform,345,52,282,62,27,true);detail.alignment=TextAnchor.MiddleCenter;detail.resizeTextForBestFit=true;detail.resizeTextMinSize=18;detail.resizeTextMaxSize=27;
   qualityLabel=Label("",panel.transform,345,8,280,24,18);qualityLabel.alignment=TextAnchor.MiddleCenter;qualityLabel.color=Muted;
   available=Label("",panel.transform,345,-36,280,28,21);available.alignment=TextAnchor.MiddleCenter;
   carried=Label("",panel.transform,345,-66,280,28,21);carried.alignment=TextAnchor.MiddleCenter;
   Line(panel.transform,345,-97,262,1,Bronze);
   var amountTitle=Label(T("terminal_quantity"),panel.transform,345,-124,280,26,20);amountTitle.alignment=TextAnchor.MiddleCenter;
   quantity=ui.CreateInputField(panel.transform,Center,Center,new Vector2(345,-164),InputField.ContentType.IntegerNumber,"1",24,120,44).GetComponent<InputField>();quantity.characterLimit=5;quantity.text="1";quantity.textComponent.alignment=TextAnchor.MiddleCenter;
   minus=Button("−",panel.transform,255,-164,44,44,()=>ChangeAmount(-1));plus=Button("+",panel.transform,435,-164,44,44,()=>ChangeAmount(1));
   stack=Button(T("terminal_stack"),panel.transform,345,-207,116,32,()=>SetAmount(selected?.Item.m_itemData.m_shared.m_maxStackSize??1));
   take=Button(T("terminal_take"),panel.transform,345,-255,282,48,Take);
   status=Label("",panel.transform,0,-296,980,24,17);status.alignment=TextAnchor.MiddleCenter;status.color=Muted;
   query="";search.onValueChanged.AddListener(value=>{query=value;Filter(true);});nextRefresh=0;dirtySlots=true;
  }
  void Refresh(){
   if(!core||!player)return;
   string name=NetworkName.For(core.GetComponent<NetworkMember>());heading.text=name.Length>0?name:T("terminal_title");
   entries=StorageIndex.Browse(core,player.GetPlayerID(),accessPoint.transform.position).GroupBy(s=>(s.Item,s.Quality)).Select(g=>{
    var prefab=ZNetScene.instance.GetPrefab(g.Key.Item);var item=prefab?prefab.GetComponent<ItemDrop>():null;
    return item?new Entry{Id=g.Key.Item,Quality=g.Key.Quality,Count=(int)Math.Min(int.MaxValue,g.Sum(s=>(long)s.Amount)),Item=item,Name=Localization.instance.Localize(item.m_itemData.m_shared.m_name)}:null;
   }).Where(e=>e!=null&&e.Count>0).OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(e=>e.Quality).ThenBy(e=>e.Id,StringComparer.Ordinal).ToList();
   if(selectedKey!=null){var found=entries.FirstOrDefault(e=>e.Key==selectedKey);if(found!=null)selected=found;else if(selected!=null)selected.Count=0;}
   Filter(false);
  }
  void Filter(bool reset){
   var previous=filtered;filtered=entries.Where(e=>TerminalRules.Search(e.Name,query)).ToList();
   float offset=reset?0:TerminalGrid.PreserveOffset(previous.Select(e=>e.Key).ToArray(),filtered.Select(e=>e.Key).ToArray(),content.anchoredPosition.y);
   scroll.StopMovement();content.sizeDelta=new Vector2(TerminalGrid.Width,TerminalGrid.Height(filtered.Count));content.anchoredPosition=new Vector2(0,offset);dirtySlots=true;
   empty.text=filtered.Count==0?T(entries.Count==0?"terminal_empty":"terminal_no_results"):"";
  }
  void RenderSlots(){
   int first=TerminalGrid.FirstIndex(content.anchoredPosition.y,filtered.Count);
   if(first==visibleStart&&!dirtySlots)return;visibleStart=first;dirtySlots=false;
   for(int i=0;i<slots.Count;i++){
    var slot=slots[i];int index=first+i;bool filled=index<filtered.Count;bool visible=index<Math.Max(TerminalGrid.Columns*TerminalGrid.VisibleRows,filtered.Count);slot.Object.SetActive(visible);if(!visible)continue;
    var next=filled?filtered[index]:null;bool changed=slot.Entry?.Key!=next?.Key;
    slot.Entry=next;slot.Rect.anchoredPosition=new Vector2(TerminalGrid.Padding+(index%TerminalGrid.Columns)*TerminalGrid.Pitch+TerminalGrid.Cell/2,-TerminalGrid.Padding-(index/TerminalGrid.Columns)*TerminalGrid.Pitch-TerminalGrid.Cell/2);
    slot.Icon.enabled=filled;slot.Button.interactable=filled;
    if(filled)slot.Icon.sprite=slot.Entry.Item.m_itemData.GetIcon();
    slot.Count.text=filled?slot.Entry.Count.ToString("N0"):"";slot.Quality.text=filled&&slot.Entry.Quality>1?"★ "+slot.Entry.Quality:"";
    slot.Selection.gameObject.SetActive(filled&&slot.Entry.Key==selectedKey);
    if(slot.Tooltip){
     // Recycling a hovered slot must not keep the previous item's tooltip alive.
     if(changed){slot.Tooltip.enabled=false;slot.Tooltip.enabled=filled;}
     slot.Tooltip.m_topic=filled?slot.Entry.Name:"";
     slot.Tooltip.m_text=filled?Localization.instance.Localize(slot.Entry.Item.m_itemData.m_shared.m_description)+(slot.Entry.Quality>1?"\n"+T("terminal_quality",slot.Entry.Quality):""):"";
    }
   }
  }
  void Select(Entry entry){if(entry==null||TerminalTransfer.Busy)return;selected=entry;selectedKey=entry.Key;SetAmount(1);dirtySlots=true;UpdateDetail();}
  void SetAmount(int value){quantity.text=Mathf.Clamp(value,1,Math.Max(1,Math.Min(selected?.Count??1,TerminalRules.MaxAmount))).ToString();}
  void ChangeAmount(int delta){int.TryParse(quantity.text,out int value);SetAmount(value+delta);}
  void UpdateDetail(){
   bool chosen=selected!=null;selectedIcon.enabled=chosen;if(chosen)selectedIcon.sprite=selected.Item.m_itemData.GetIcon();
   detail.text=chosen?selected.Name:T("terminal_select");qualityLabel.text=chosen&&selected.Quality>1?T("terminal_quality",selected.Quality):"";
   available.text=chosen?T("terminal_available",selected.Count.ToString("N0")):"";
   int held=chosen?player.GetInventory().GetAllItems().Where(i=>i.m_dropPrefab&&i.m_dropPrefab.name==selected.Id&&i.m_quality==selected.Quality).Sum(i=>i.m_stack):0;
   carried.text=chosen?T("terminal_carried",held.ToString("N0")):"";
   bool valid=TerminalRules.Quantity(quantity.text,selected?.Count??0,out int amount);bool editable=chosen&&selected.Count>0&&!TerminalTransfer.Busy;
   take.interactable=editable&&valid;quantity.interactable=editable;minus.interactable=editable;plus.interactable=editable;stack.interactable=editable;search.interactable=!TerminalTransfer.Busy;
   var label=take.GetComponentInChildren<Text>();if(label)label.text=T(TerminalTransfer.Busy?"terminal_pending":"terminal_take");
   if(Time.unscaledTime>=statusUntil)status.text=T(TerminalTransfer.Busy?"terminal_pending":"terminal_close");
  }
  void Take(){
   if(selected==null||!TerminalRules.Quantity(quantity.text,selected.Count,out int amount)||TerminalTransfer.Busy)return;
   if(!TerminalTransfer.Start(accessPoint,core,player,selected.Id,selected.Quality,amount))TransferStatus("terminal_retry");else TransferStatus("terminal_pending");
   UpdateDetail();
  }
  internal static void TransferStatus(string key,params object[] args){if(!instance||!instance.panel)return;instance.status.text=T(key,args);instance.statusUntil=Time.unscaledTime+5;instance.nextRefresh=0;}
 }
}
