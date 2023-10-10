using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VF.Model.Conditions;

namespace VF.Inspector {

public class VRCFuryConditionEditor {

    private const string menuPathTooltip = "Menu Path is where you'd like the toggle to be located in the menu. This is unrelated"
        + " to the menu filenames -- simply enter the title you'd like to use. If you'd like the toggle to be in a submenu, use slashes. For example:\n\n"
        + "If you want the toggle to be called 'Shirt' in the root menu, you'd put:\nShirt\n\n"
        + "If you want the toggle to be called 'Pants' in a submenu called 'Clothing', you'd put:\nClothing/Pants";

    public static VisualElement render(
        SerializedProperty prop,
        string myLabel = null,
        int labelWidth = 100,
        string tooltip = null
    ) {
        var container = new VisualElement();
        container.AddToClassList("vfCondition");

        var list = prop.FindPropertyRelative("orConditions");

        void OnPlusOuter() {
            VRCFuryEditorUtils.AddToList(list, entry => entry.managedReferenceValue = new AndCondition());
        }

        container.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var body = new VisualElement();


            var showPlus = list.arraySize == 0;
            var showList = list.arraySize > 0;

            if (showPlus) {
                var segments = new VisualElement();
                body.Add(segments);
                segments.style.flexDirection = FlexDirection.Row;
                segments.style.alignItems = Align.FlexStart;

                if (showPlus) {
                    var plus = VRCFuryEditorUtils.Button("Add Condition +", OnPlusOuter);
                    plus.style.flexGrow =  1;
                    plus.style.flexBasis = 20;
                    segments.Add(plus);
                }
            }
            if (showList) {
                body.Add(VRCFuryEditorUtils.List(list, onPlus: OnPlusOuter, inBetween: "-- OR --"));
            }

            return VRCFuryEditorUtils.AssembleProp(myLabel, tooltip, body, false, showList, labelWidth);
        }, list));

        return container;
    }

    public static VisualElement renderAnd(
        SerializedProperty prop,
        string myLabel = null,
        int labelWidth = 100,
        string tooltip = null
    ) {
        var container = new VisualElement();
        container.AddToClassList("vfAndCondition");

        var list = prop.FindPropertyRelative("andCondition");

        void OnPlusInner() {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Menu Trigger"), false,
                () => { VRCFuryEditorUtils.AddToList(list, entry => entry.managedReferenceValue = new MenuTrigger()); });
            menu.AddItem(new GUIContent("Global Trigger"), false,
                () => { VRCFuryEditorUtils.AddToList(list, entry => entry.managedReferenceValue = new GlobalTrigger()); });
            menu.AddItem(new GUIContent("Gesture Trigger"), false,
                () => { VRCFuryEditorUtils.AddToList(list, entry => entry.managedReferenceValue = new GestureTrigger()); });
            menu.ShowAsContext();
        }

        container.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var body = new VisualElement();


            var showPlus = list.arraySize == 0;
            var showList = list.arraySize > 0;

            if (showPlus) {
                var segments = new VisualElement();
                body.Add(segments);
                segments.style.flexDirection = FlexDirection.Row;
                segments.style.alignItems = Align.FlexStart;

                if (showPlus) {
                    var plus = VRCFuryEditorUtils.Button("Add Trigger +", OnPlusInner);
                    plus.style.flexGrow = 1;
                    plus.style.flexBasis = 20;
                    segments.Add(plus);
                }
            }
            if (showList) {
                body.Add(VRCFuryEditorUtils.List(list, onPlus: OnPlusInner, inBetween: "-- AND --"));
            }

            return VRCFuryEditorUtils.AssembleProp(myLabel, tooltip, body, false, showList, labelWidth);
        }, list));

        return container;
    }

    public static VisualElement renderTrigger(
        SerializedProperty prop,
        string myLabel = null,
        int labelWidth = 100,
        string tooltip = null
    ) {
        var container = new VisualElement();
        container.AddToClassList("vfTrigger");

        switch (prop.type) {
            case "managedReference<MenuTrigger>":
                var menu = VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("menuPath"), "Menu Path", tooltip: menuPathTooltip);
                container.Add(menu);
                break;
            case "managedReference<GlobalTrigger>":
                container.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("boolName"), "Global Parameter"));
                break;
            case "managedReference<GestureTrigger>":            
                var handProp = prop.FindPropertyRelative("hand");
                var signProp = prop.FindPropertyRelative("sign");
                var comboSignProp = prop.FindPropertyRelative("comboSign");

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var hand = VRCFuryEditorUtils.Prop(handProp);
                hand.style.flexBasis = 70;
                row.Add(hand);
                var handSigns = VRCFuryEditorUtils.RefreshOnChange(() => {
                    var w = new VisualElement();
                    w.style.flexDirection = FlexDirection.Row;
                    w.style.alignItems = Align.Center;
                    var leftBox = VRCFuryEditorUtils.Prop(signProp);
                    var rightBox = VRCFuryEditorUtils.Prop(comboSignProp);
                    if ((GestureTrigger.Hand)handProp.enumValueIndex == GestureTrigger.Hand.COMBO) {
                        w.Add(new Label("L") { style = { flexBasis = 10 }});
                        leftBox.style.flexGrow = 1;
                        leftBox.style.flexShrink = 1;
                        w.Add(leftBox);
                        w.Add(new Label("R") { style = { flexBasis = 10 }});
                        rightBox.style.flexGrow = 1;
                        rightBox.style.flexShrink = 1;
                        w.Add(rightBox);
                    } else {
                        leftBox.style.flexGrow = 1;
                        leftBox.style.flexShrink = 1;
                        w.Add(leftBox);
                    }

                    return w;
                }, handProp);
                handSigns.style.flexGrow = 1;
                row.Add(handSigns);
                container.Add(row);
                break;
        }

        return container;
    }
}

}
