using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VF.Feature.Base;
using VF.Inspector;
using VF.Model.Feature;
using VF.Utils;

namespace VF.Feature {
    internal class SetIconBuilder : FeatureBuilder<SetIcon> {
        public override string GetEditorTitle() {
            return "Override Menu Icon";
        }
        
        public override VisualElement CreateEditor(SerializedProperty prop) {
            var content = new VisualElement();
            content.Add(VRCFuryEditorUtils.Info("This feature will override the menu icon for the given menu path. You can use slashes to look in subfolders."));

            var pathProp = prop.FindPropertyRelative("path");
            content.Add(MoveMenuItemBuilder.SelectButton(avatarObject, false, pathProp));
            
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("icon"), "Icon"));
            return content;
        }
    }
}
