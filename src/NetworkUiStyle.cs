using System;
using System.Linq;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RunicStorageNetwork {
 internal static class NetworkUiStyle {
  internal static readonly Color Gold=new Color(1f,.79f,.42f),Bronze=new Color(.48f,.37f,.22f);
  // Valheim gives each GUI screen its own root canvas (Canvas+CanvasScaler+GuiScaler+
  // GraphicRaycaster) and scales it with the render resolution and the GUI scale setting.
  // Jotunn's CustomGUIFront is a sibling root canvas without a GuiScaler, so its factor
  // stays 1. Take the same parent and own the scaling instead of inheriting none.
  internal static CanvasScaler Screen(string name,int order){
   var parent=GUIManager.CustomGUIFront?GUIManager.CustomGUIFront.transform.parent:null;
   if(!parent)throw new InvalidOperationException("Jotunn's custom GUI has no scalable parent");
   var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
   go.layer=LayerMask.NameToLayer("UI");go.transform.SetParent(parent,false);go.transform.SetAsLastSibling();
   var rect=(RectTransform)go.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
   var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=order;
   canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
   // Matches Jotunn's own canvas, so the panel sprites keep the borders they were tuned for.
   var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.referencePixelsPerUnit=50;
   return scaler;
  }
  internal static GameObject Panel(Transform parent,float width,float height){
   var center=new Vector2(.5f,.5f);
   var panel=GUIManager.Instance.CreateWoodpanel(parent,center,center,Vector2.zero,width,height,false);
   panel.GetComponent<Image>().color=new Color(.24f,.24f,.24f,1);
   Edge(panel.transform,new Vector2(0,0),new Vector2(1,0),new Vector2(6,6),new Vector2(-6,7));
   Edge(panel.transform,new Vector2(0,1),new Vector2(1,1),new Vector2(6,-7),new Vector2(-6,-6));
   Edge(panel.transform,new Vector2(0,0),new Vector2(0,1),new Vector2(6,6),new Vector2(7,-6));
   Edge(panel.transform,new Vector2(1,0),new Vector2(1,1),new Vector2(-7,6),new Vector2(-6,-6));
   return panel;
  }
  static void Edge(Transform parent,Vector2 min,Vector2 max,Vector2 insetMin,Vector2 insetMax){
   var go=new GameObject("Frame",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
   var rect=go.GetComponent<RectTransform>();rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=insetMin;rect.offsetMax=insetMax;
   var image=go.GetComponent<Image>();image.color=Bronze;image.raycastTarget=false;
  }
 }
 // Only reskin the native prompt while it belongs to a network core. Native
 // input, confirmation, cancellation and TextReceiver validation remain intact.
 public sealed class NetworkRenameStyle:MonoBehaviour {
  Image original;bool originalEnabled;TMP_Text title;Color titleColor;GameObject backdrop;
  internal static void Reset(TextInput input){
   if(input&&input.m_panel){var style=input.m_panel.GetComponent<NetworkRenameStyle>();if(style)style.Restore();}
  }
  internal static void Apply(TextInput input){
   if(!input||!input.m_panel||!input.m_inputField)return;
   var style=input.m_panel.GetComponent<NetworkRenameStyle>()??input.m_panel.AddComponent<NetworkRenameStyle>();
   try{style.Create(input);}catch(Exception e){style.Restore();Plugin.Error("network rename style",e);}
  }
  void Create(TextInput input){
   Restore();var field=input.m_inputField.GetComponent<RectTransform>();
   // Choose the smallest non-interactive background enclosing the input and
   // heading, excluding a possible full-screen dimmer and button graphics.
   var candidates=input.m_panel.GetComponentsInChildren<Image>(true).Where(image=>image.sprite&&!image.GetComponentInParent<Selectable>()).Where(image=>{
    var rect=image.rectTransform;
    var point=rect.InverseTransformPoint(field.TransformPoint(field.rect.center));
    bool contains=rect.rect.Contains(new Vector2(point.x,point.y));
    if(input.m_topic){point=rect.InverseTransformPoint(input.m_topic.rectTransform.TransformPoint(input.m_topic.rectTransform.rect.center));contains&=rect.rect.Contains(new Vector2(point.x,point.y));}
    return contains&&rect.rect.width>=field.rect.width&&rect.rect.height>=field.rect.height*2;
   }).OrderBy(image=>image.rectTransform.rect.width*image.rectTransform.rect.height);
   original=candidates.FirstOrDefault();
   if(!original)throw new InvalidOperationException("Native rename dialog background was not found");
   originalEnabled=original.enabled;
   var area=original.rectTransform;backdrop=NetworkUiStyle.Panel(area,area.rect.width,area.rect.height);backdrop.name="RSN_RenameBackground";backdrop.transform.SetAsFirstSibling();
   var size=backdrop.GetComponent<RectTransform>();size.anchorMin=Vector2.zero;size.anchorMax=Vector2.one;size.offsetMin=size.offsetMax=Vector2.zero;
   original.enabled=false;
   title=input.m_topic;if(title){titleColor=title.color;title.color=NetworkUiStyle.Gold;}
  }
  void Restore(){
   if(original)original.enabled=originalEnabled;if(title)title.color=titleColor;
   if(backdrop){backdrop.SetActive(false);Destroy(backdrop);}
   original=null;title=null;backdrop=null;
  }
  void OnDisable(){Restore();}
  void OnDestroy(){Restore();}
 }
}
