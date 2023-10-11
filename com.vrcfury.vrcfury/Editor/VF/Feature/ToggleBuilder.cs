using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VF.Builder;
using VF.Feature.Base;
using VF.Injector;
using VF.Inspector;
using VF.Model;
using VF.Model.Conditions;
using VF.Model.StateAction;
using VF.Service;
using VF.Utils;
using VF.Utils.Controller;
using Toggle = VF.Model.Feature.Toggle;

namespace VF.Feature {

public class ToggleBuilder : FeatureBuilder<Toggle> {

    private VFAParam param;
    private string layerName;
    private VFCondition onCase;
    private bool onEqualsOut;

    private bool addMenuItem;
    private bool useInt;
    private int intTarget = -1;
    private bool enableExclusiveTag;

    private string humanoidMask = null;
    private string primaryExclusive = null;

    [VFAutowired] private readonly ActionClipService actionClipService;
    [VFAutowired] private readonly PhysboneResetService physboneResetService;

    private AnimationClip restingClip;

    private const string menuPathTooltip = "Menu Path is where you'd like the toggle to be located in the menu. This is unrelated"
        + " to the menu filenames -- simply enter the title you'd like to use. If you'd like the toggle to be in a submenu, use slashes. For example:\n\n"
        + "If you want the toggle to be called 'Shirt' in the root menu, you'd put:\nShirt\n\n"
        + "If you want the toggle to be called 'Pants' in a submenu called 'Clothing', you'd put:\nClothing/Pants";

    public ISet<string> GetExclusives(string objects) {
        return objects.Split(',')
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToImmutableHashSet();
    }
	
    public ISet<string> GetExclusiveTags() {
        CheckHumanoidMask();
        var tags = model.enableExclusiveTag ? model.exclusiveTag : "";
        if (humanoidMask == "emote") {
            tags += ",VF_EMOTE";
        }
        if (humanoidMask == "leftHand" || humanoidMask == "hands") {
            tags += ",VF_LEFT_HAND";
        }
        if (humanoidMask == "rightHand" || humanoidMask == "hands") {
            tags += ",VF_RIGHT_HAND";
        }
        return GetExclusives(tags);
    }

    public ISet<string> GetGlobalParams() {
        if(model.enableDriveGlobalParam)
            return GetExclusives(model.driveGlobalParam);
        return new HashSet<string>(); 
    }
    public VFAParam GetParam() {
        return param;
    }

    private string GetHumanoidMaskName(params State[] states) {
        var leftHand = false;
        var rightHand = false;
        foreach (var state in states) {
            if (state == null || state.actions.Count() == 0) continue;
            foreach(var action in state.actions) {
                if (action is AnimationClipAction actionClip) {
                    var muscleTypes = new AnimatorIterator.Clips().From(actionClip.clip.Get())
                        .SelectMany(clip => clip.GetMuscleBindingTypes())
                        .ToImmutableHashSet();

                    if (muscleTypes.Contains(EditorCurveBindingExtensions.MuscleBindingType.Other))
                        return "emote";
                    if (muscleTypes.Contains(EditorCurveBindingExtensions.MuscleBindingType.LeftHand))
                        leftHand = true;
                    if (muscleTypes.Contains(EditorCurveBindingExtensions.MuscleBindingType.RightHand))
                        rightHand = true;
                }
            }
        }

        if (leftHand && rightHand) return "hands";
        if (leftHand) return "leftHand";
        if (rightHand) return "rightHand";
        return "none";
    }

    private VFLayer GetLayer(string layerName) {
        if (enableExclusiveTag) {
            var primaryExclusiveTag = GetPrimaryExclusive();
            if (primaryExclusiveTag == "") return GetFx().NewLayer(layerName);
            if (!exclusiveAnimationLayers.ContainsKey(primaryExclusiveTag)) {
                 exclusiveAnimationLayers[primaryExclusiveTag] = GetFx().NewLayer((primaryExclusiveTag + " Animations").Trim());
            }
            return exclusiveAnimationLayers[primaryExclusiveTag];
        }
        return GetFx().NewLayer(layerName);
    }

    private VFLayer GetLayerForParameters(string exclusiveTag) {
        if (!exclusiveParameterLayers.ContainsKey(exclusiveTag)) {
            exclusiveParameterLayers[exclusiveTag] = GetFx().NewLayer(exclusiveTag + " Parameters");
            exclusiveParameterLayers[exclusiveTag].NewState("Default");
        }
        return exclusiveParameterLayers[exclusiveTag];
    }

    private VFState GetOffState(string stateName, VFLayer layer) {
        if (layer.GetRawStateMachine().states.Count() > 0) {
            return new VFState(layer.GetRawStateMachine().states.First(), layer.GetRawStateMachine());
        }
        return layer.NewState(stateName);
    }

    private void SetStartState(VFLayer layer, AnimatorState state) {
        layer.GetRawStateMachine().defaultState = state;
    }

    public string GetPrimaryExclusive() {
        if (primaryExclusive == null) {
            string targetTag = "";
            int targetMax = -1;

            foreach (var exclusiveTag in GetExclusiveTags()) {
                int tagCount = 1;
                foreach (var toggle in allBuildersInRun
                            .OfType<ToggleBuilder>()) {

                    if (!toggle.model.exclusiveOffState && toggle.GetName().Item3 && toggle.GetExclusiveTags().Contains(exclusiveTag)) {
                        tagCount++;
                    }
                }

                if (tagCount > targetMax) {
                    targetTag = exclusiveTag;
                    targetMax = tagCount;
                }
            }

            primaryExclusive = targetTag;
        }
        return primaryExclusive;
    }

    private void CheckHumanoidMask() {
        if (humanoidMask != null) return;

        humanoidMask = GetHumanoidMaskName(model.state, model.localState, model.transitionStateIn, model.localTransitionStateIn, model.transitionStateOut, model.localTransitionStateOut);
        if (humanoidMask != "none") enableExclusiveTag = true;
    }

    private void CheckExclusives() {
        if (!enableExclusiveTag) return;

        string targetTag = GetPrimaryExclusive();
        int tagCount = 1;
        int tagIndex = 0;

       
        foreach (var toggle in allBuildersInRun
                    .OfType<ToggleBuilder>()) {

            if (!model.exclusiveOffState && toggle == this) {
                tagIndex = tagCount;
            }
            if (!toggle.model.exclusiveOffState && toggle.GetName().Item3 && toggle.GetPrimaryExclusive() == targetTag) {
                tagCount++;
            }
        }

        if (tagCount > 256) {
            throw new Exception("Too many toggles for exclusive tag " + targetTag + ". Please reduce the number of toggles using this tag to below 255.");
        }

        if (tagCount > 8) {
            useInt = true;
            intTarget = tagIndex;
        }
    }

    private (string,bool,bool) GetName() {
        if (model.paramOverride != null) {
            return (model.paramOverride, false, false);
        }
        foreach (var or in model.condition.orConditions) {
            foreach (var trig in or.andCondition) {
                var menuTrigger = trig as MenuTrigger;
                if (menuTrigger != null && !string.IsNullOrEmpty(menuTrigger.menuPath)) {
                    return (menuTrigger.menuPath, model.usePrefixOnParam, true);
                }
            }
        }
        foreach (var or in model.condition.orConditions) {
            foreach (var trig in or.andCondition) {
                var gestrueTrigger = trig as GestureTrigger;
                if (gestrueTrigger != null) {
                    var temp = "Gesture ";
                    if (gestrueTrigger.hand != GestureTrigger.Hand.COMBO) {
                        temp += "Left = " + gestrueTrigger.sign + ", Right = " + gestrueTrigger.comboSign;
                    } else {
                        if (gestrueTrigger.hand != GestureTrigger.Hand.EITHER) temp += "Either = ";
                        if (gestrueTrigger.hand != GestureTrigger.Hand.LEFT) temp += "Left = ";
                        if (gestrueTrigger.hand != GestureTrigger.Hand.RIGHT) temp += "Right = ";

                        temp += gestrueTrigger.sign;
                    }
                    return (temp, false, false);
                }
            }
        }
        foreach (var or in model.condition.orConditions) {
            foreach (var trig in or.andCondition) {
                var globalTrigger = trig as GlobalTrigger;
                if (globalTrigger != null && !string.IsNullOrEmpty(globalTrigger.boolName)) {
                    return (globalTrigger.boolName, false, false);
                }
            }
        }
        return (null, false, false);
    }

    private VFCondition GetOnCase(VFCondition menuCondition) {
        var fx = GetFx();

        VFCondition EvaluateTrigger(Trigger t) {
            
            var GestureLeft = fx.GestureLeft();
            var GestureRight = fx.GestureRight();
        
            switch(t) {
                case MenuTrigger mt:
                    return menuCondition;
                case GlobalTrigger gt:
                    return fx.NewBool(gt.boolName, usePrefix: false).IsTrue();
                case GestureTrigger gt:
                    if (gt.hand == GestureTrigger.Hand.LEFT) {
                        return GestureLeft.IsEqualTo((int)gt.sign);
                    } else if (gt.hand == GestureTrigger.Hand.RIGHT) {
                        return GestureRight.IsEqualTo((int)gt.sign);
                    } else if (gt.hand == GestureTrigger.Hand.EITHER) {
                        return GestureLeft.IsEqualTo((int)gt.sign).Or(GestureRight.IsEqualTo((int)gt.sign));
                    } else if (gt.hand == GestureTrigger.Hand.COMBO) {
                        return GestureLeft.IsEqualTo((int)gt.sign).And(GestureRight.IsEqualTo((int)gt.comboSign));
                    }
                return fx.Never();
            }
            return fx.Never();
        }

        List<AndCondition> usefulOrClauses = new List<AndCondition>();

        foreach (var andClause in model.condition.orConditions) {
            if (andClause.andCondition.Count() > 0) {
                usefulOrClauses.Add(andClause);
            }
        }

        var firstTrigger = usefulOrClauses.First().andCondition.First();
        var output = EvaluateTrigger(firstTrigger);

        foreach(var andTrigger in usefulOrClauses.First().andCondition.Skip(1)) {
            output = output.And(EvaluateTrigger(andTrigger));
        }

        if (usefulOrClauses.First().andCondition.Any(trig => trig is GestureTrigger)) {
            previousGestures.Add(output);
            foreach (var con in previousGestures.Take(previousGestures.Count() - 1)) {
                output = output.And(con.Not());
            }
            foreach (var tag in GetExclusiveTags()) {
                var lockParam = fx.NewBool("VF_" + tag + "_Lock", usePrefix: false);
                output = output.And(lockParam.IsFalse());
            }
        }

        foreach (var orCond in usefulOrClauses.Skip(1)) {
            var temp = EvaluateTrigger(orCond.andCondition.First());
            foreach (var andTrigger in orCond.andCondition.Skip(1)) {
                temp = temp.And(EvaluateTrigger(andTrigger));
            }
            if (orCond.andCondition.Any(trig => trig is GestureTrigger)) {
                previousGestures.Add(temp);
                foreach (var con in previousGestures.Take(previousGestures.Count() - 1)) {
                    temp = temp.And(con.Not());
                }
                foreach (var tag in GetExclusiveTags()) {
                    var lockParam = fx.NewBool("VF_" + tag + "_Lock", usePrefix: false);
                    temp = temp.And(lockParam.IsFalse());
                }
            }
            output = output.Or(temp);
        }

        return output;
    }

    private void CreateSlider() {
        bool usePrefixOnParam;
        (layerName, usePrefixOnParam, addMenuItem) = GetName();

        var fx = GetFx();
        var layer = fx.NewLayer(layerName);

        var off = layer.NewState("Off");
        var on = layer.NewState("On");
        var x = fx.NewFloat(
            layerName,
            synced: addMenuItem,
            saved: model.saved,
            def: model.defaultOn ? model.defaultSliderValue : 0,
            usePrefix: usePrefixOnParam
        );

        var hasTitle = !string.IsNullOrEmpty(layerName);
        var hasIcon = model.enableIcon && model.icon?.Get() != null;
        if (addMenuItem && (hasTitle || hasIcon)) {
            manager.GetMenu().NewMenuSlider(
                layerName,
                x,
                icon: model.enableIcon ? model.icon?.Get() : null
            );
        }

        var clip = actionClipService.LoadState("On", model.state);
        if (ClipBuilderService.IsStaticMotion(clip)) {
            var tree = fx.NewBlendTree("On Tree");
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.blendParameter = x.Name();
            tree.AddChild(fx.GetEmptyClip(), 0);
            tree.AddChild(clip, 1);
            on.WithAnimation(tree);
        } else {
            on.WithAnimation(clip).MotionTime(x);
        }

        var isOn = x.IsGreaterThan(0);
        off.TransitionsTo(on).When(isOn);
        on.TransitionsTo(off).When(isOn.Not());
    }


    [FeatureBuilderAction]
    public void Apply() {
        
        useInt = model.useInt;
        addMenuItem = model.addMenuItem;
        enableExclusiveTag = model.enableExclusiveTag || enableExclusiveTag;

        if (model.condition.orConditions.Count() == 0 || !model.condition.orConditions.Any(orCond => orCond.andCondition.Count() > 0)) {
            return;
        }

        if (model.slider) {
            CreateSlider();
            return;
        }
        bool usePrefixOnParam;
        (layerName, usePrefixOnParam, addMenuItem) = GetName();

        var physBoneResetter = physboneResetService.CreatePhysBoneResetter(model.resetPhysbones, layerName);

        var hasIcon = model.enableIcon && model.icon?.Get() != null;

        CheckHumanoidMask();
        CheckExclusives();

        var fx = GetFx();
        var layer = GetLayer(layerName);
        var off = GetOffState("Off", layer);

        VFCondition paramOnCase = null;
        
        if (layerName != null && addMenuItem) {
            if (useInt) {
                if (intTarget == -1) {
                    var numParam = fx.NewInt(layerName, synced: true, saved: model.saved, def: model.defaultOn ? 1 : 0, usePrefix: usePrefixOnParam);
                    paramOnCase = numParam.IsNotEqualTo(0);
                } else {
                    var numParam = fx.NewInt("VF_" + GetPrimaryExclusive() + "_Exclusives", synced: true, saved: model.saved, def: model.defaultOn ? intTarget : 0, usePrefix: false);
                    paramOnCase = numParam.IsEqualTo(intTarget);
                    param = numParam;
                }
            } else {
                var boolParam = fx.NewBool(layerName, synced: true, saved: model.saved, def: model.defaultOn, usePrefix: usePrefixOnParam);
                param = boolParam;
                paramOnCase = boolParam.IsTrue();
            }
            foreach(var orCond in model.condition.orConditions) {
                foreach (var trig in orCond.andCondition) {
                    var mt = trig as MenuTrigger;
                    if (mt != null && !string.IsNullOrEmpty(mt.menuPath)) {
                        if (model.holdButton) {
                            manager.GetMenu().NewMenuButton(
                                mt.menuPath,
                                param,
                                icon: model.icon?.Get(),
                                value: intTarget != -1 ? intTarget : 1
                            );
                        } else {
                            manager.GetMenu().NewMenuToggle(
                                mt.menuPath,
                                param,
                                icon: model.icon?.Get(),
                                value: intTarget != -1 ? intTarget : 1
                            );
                        }
                    }
                }
            }
        }

        onCase = GetOnCase(paramOnCase);

        if (model.securityEnabled) {
            var securityLockUnlocked = allBuildersInRun
                .OfType<SecurityLockBuilder>()
                .Select(f => f.GetEnabled())
                .FirstOrDefault();
            if (securityLockUnlocked != null) {
                onCase = onCase.And(securityLockUnlocked);
            }
        }
        
        if (model.separateLocal) {
            var isLocal = fx.IsLocal().IsTrue();
            Apply(fx, layer, off, onCase.And(isLocal.Not()), layerName + " On Remote", model.state, model.transitionStateIn, model.transitionStateOut, physBoneResetter);
            Apply(fx, layer, off, onCase.And(isLocal), layerName + " On Local", model.localState, model.localTransitionStateIn, model.localTransitionStateOut, physBoneResetter);
        } else {
            Apply(fx, layer, off, onCase, layerName + " On", model.state, model.transitionStateIn, model.transitionStateOut, physBoneResetter);
        }

    }

    private void Apply(
        ControllerManager fx,
        VFLayer layer,
        VFState off,
        VFCondition onCase,
        string onName,
        State action,
        State inAction,
        State outAction,
        VFABool physBoneResetter
    ) {

        var transitionTime = model.transitionTime;
        if (!model.hasTransitionTime)  transitionTime = -1;

        var clip = actionClipService.LoadState(onName, action);

        VFState inState;
        VFState onState;
        VFState outState;

        if (model.hasTransition && inAction != null && inAction.actions.Count() != 0) {
            var transitionClipIn = actionClipService.LoadState(onName + " In", inAction);

            // if clip is empty, copy last frame of transition
            if (clip == fx.GetEmptyClip()) {
                clip = fx.NewClip(onName);
                clip.CopyFromLast(transitionClipIn);
            }

            inState = layer.NewState(onName + " In").WithAnimation(transitionClipIn);
            onState = layer.NewState(onName).WithAnimation(clip);
            inState.TransitionsTo(onState).When().WithTransitionExitTime(1);

        } else {
            inState = onState = layer.NewState(onName).WithAnimation(clip);
        }

        off.TransitionsToExit().When(onCase).WithTransitionDurationSeconds(transitionTime);
        inState.TransitionsFromEntry().When(onCase);

        if (model.simpleOutTransition) outAction = inAction;

        if (model.hasTransition && outAction != null && outAction.actions.Count() != 0) {
            var transitionClipOut = actionClipService.LoadState(onName + " Out", outAction);
            outState = layer.NewState(onName + " Out").WithAnimation(transitionClipOut).Speed(model.simpleOutTransition ? -1 : 1);
            onState.TransitionsTo(outState).When(onCase.Not()).WithTransitionDurationSeconds(transitionTime).WithTransitionExitTime(model.hasExitTime ? 1 : -1);
        } else {
            outState = onState;
        }

        onEqualsOut = outState == onState;

        var exitTransition = outState.TransitionsToExit();

        if (onEqualsOut) {
            exitTransition.When(onCase.Not()).WithTransitionDurationSeconds(transitionTime).WithTransitionExitTime(model.hasExitTime ? 1 : -1);
        } else {
            exitTransition.When().WithTransitionExitTime(1);
        }

        if (physBoneResetter != null) {
            off.Drives(physBoneResetter, true);
            inState.Drives(physBoneResetter, true);
        }

        if (model.enableDriveGlobalParam && !string.IsNullOrWhiteSpace(model.driveGlobalParam)) {
            foreach(var p in GetGlobalParams()) {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var driveGlobal = fx.NewBool(
                    p,
                    synced: false,
                    saved: false,
                    def: false,
                    usePrefix: false
                );
                off.Drives(driveGlobal, false);
                inState.Drives(driveGlobal, true);
            }
        }

        if (restingClip == null && model.includeInRest) {
            restingClip = clip;

            if (model.defaultOn) {
                SetStartState(layer, onState.GetRaw());
                if (!enableExclusiveTag) {
                    off.TransitionsFromEntry().When(fx.Always());
                }
            }
        }
    }

    [FeatureBuilderAction(FeatureOrder.CollectToggleExclusiveTags)]
    public void ApplyExclusiveTags() {
        
        if (!enableExclusiveTag) return;

        var fx = GetFx();
        var paramsToTurnOff = new HashSet<VFABool>();
        var paramsToTurnToZero = new Dictionary<string, HashSet<(VFAInteger, int)>>();
        var allOthersOff = fx.Always();
        var isLocal = fx.IsLocal().IsTrue();
            
        foreach (var exclusiveTag in GetExclusiveTags()) {
            foreach (var other in allBuildersInRun
                        .OfType<ToggleBuilder>()
                        .Where(b => b != this)) {
                if (other.GetExclusiveTags().Contains(exclusiveTag)) {
                    var otherParam = other.GetParam();
                    if (otherParam != null) {
                        var otherOnCondition = other.useInt ? (otherParam as VFAInteger).IsEqualTo(other.intTarget) : (otherParam as VFABool).IsTrue();
                        if (other.useInt) {
                            if (param.Name() != otherParam.Name()) {
                                if (!paramsToTurnToZero.ContainsKey(otherParam.Name())) {
                                    paramsToTurnToZero[otherParam.Name()] = new HashSet<(VFAInteger, int)>();
                                }
                                paramsToTurnToZero[otherParam.Name()].Add((otherParam as VFAInteger, other.intTarget));
                            }
                        } else {
                            paramsToTurnOff.Add(otherParam as VFABool);
                            allOthersOff = allOthersOff.And(otherOnCondition.Not());
                        }
                    }
                }
            }
        }

        if (model.includeInRest && model.defaultOn) {
            var layer = GetLayer(layerName);
            var off = GetOffState("Off", layer);
            off.TransitionsFromEntry().When(allOthersOff);
        }

        

        var exclusiveLayer = GetLayerForParameters(GetPrimaryExclusive());
        var startState = GetOffState("Default", exclusiveLayer);
        var triggerState = exclusiveLayer.NewState(layerName);
        var exitState = exclusiveLayer.NewState(layerName + " Exit");
        var onParam = useInt ? (param as VFAInteger).IsEqualTo(intTarget) : (param as VFABool).IsTrue();

        var intStates = new HashSet<(VFCondition, VFState)>();

        triggerState.TransitionsTo(exitState).When(onParam.Not());
        exitState.TransitionsToExit().When(fx.Always());

        foreach (var tag in GetExclusiveTags()) {
            var lockParam = fx.NewBool("VF_" + tag + "_Lock", usePrefix: false);
            triggerState.Drives(lockParam, true);
            exitState.Drives(lockParam, false);
        }

        var allOrParam = fx.Never();

        foreach (var tag in paramsToTurnToZero.Keys) {
            var orParam = fx.Never();
            VFAInteger tagParam = paramsToTurnToZero[tag].First().Item1;
            foreach (var (p, i) in paramsToTurnToZero[tag]) {
                orParam = orParam.Or(p.IsEqualTo(i));
            }
            var intTriggerState = exclusiveLayer.NewState(layerName + " + " + tagParam.Name());
            startState.TransitionsTo(intTriggerState).When(onParam.And(orParam));
            intTriggerState.Drives(tagParam, 0);

            intStates.Add((onParam.And(orParam), intTriggerState));
            allOrParam = allOrParam.Or(orParam);
        }

        foreach (var (condition, s1) in intStates) {
            foreach (var (dud, s2) in intStates) {
                if (s1 != s2) {
                    s1.TransitionsTo(s2).When(condition);
                }
            }
            s1.TransitionsTo(triggerState).When(allOrParam.Not());
        }

        triggerState.TransitionsFromAny().When(onParam.And(allOrParam.Not()));
            
        foreach (var p in paramsToTurnOff) {
            triggerState.Drives(p, false);
        }

        if (!useInt && model.exclusiveOffState) {
            startState.TransitionsTo(triggerState).When(allOthersOff);
            triggerState.Drives((param as VFABool), true);
        }
    }

    public override string GetClipPrefix() {
        var temp = GetName().Item1;
        if (temp == null) temp = "";
        return "Toggle " + temp.Replace('/', '_');
    }

    [FeatureBuilderAction(FeatureOrder.ApplyToggleRestingState)]
    public void ApplyRestingState() {
        if (restingClip != null) {
            var restingStateBuilder = allBuildersInRun
                .OfType<RestingStateBuilder>()
                .First();
            restingStateBuilder.ApplyClipToRestingState(restingClip, true);
        }
    }

    public override string GetEditorTitle() {
        return "Toggle";
    }

    public override VisualElement CreateEditor(SerializedProperty prop) {
        return CreateEditor(prop, content => content.Add(VRCFuryStateEditor.render(prop.FindPropertyRelative("state"))));
    }

    private static VisualElement CreateEditor(SerializedProperty prop, Action<VisualElement> renderBody) {
        var content = new VisualElement();

        var savedProp = prop.FindPropertyRelative("saved");
        var sliderProp = prop.FindPropertyRelative("slider");
        var securityEnabledProp = prop.FindPropertyRelative("securityEnabled");
        var defaultOnProp = prop.FindPropertyRelative("defaultOn");
        var includeInRestProp = prop.FindPropertyRelative("includeInRest");
        var exclusiveOffStateProp = prop.FindPropertyRelative("exclusiveOffState");
        var enableExclusiveTagProp = prop.FindPropertyRelative("enableExclusiveTag");
        var resetPhysboneProp = prop.FindPropertyRelative("resetPhysbones");
        var enableIconProp = prop.FindPropertyRelative("enableIcon");
        var enableDriveGlobalParamProp = prop.FindPropertyRelative("enableDriveGlobalParam");
        var separateLocalProp = prop.FindPropertyRelative("separateLocal");
        var hasTransitionProp = prop.FindPropertyRelative("hasTransition");
        var simpleOutTransitionProp = prop.FindPropertyRelative("simpleOutTransition");
        var defaultSliderProp = prop.FindPropertyRelative("defaultSliderValue");
        var hasTransitionTimeProp = prop.FindPropertyRelative("hasTransitionTime");
        var hasExitTimeProp = prop.FindPropertyRelative("hasExitTime");
        var holdButtonProp = prop.FindPropertyRelative("holdButton");

        var condistionsProp = prop.FindPropertyRelative("condition");

        var flex = new VisualElement {
            style = {
                flexDirection = FlexDirection.Row,
                alignItems = Align.FlexStart
            }
        };
        content.Add(flex);
        var con = VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("condition"), "Conditions");
        con.style.flexGrow = 1;
        flex.Add(con);

        var button = VRCFuryEditorUtils.Button("Options", () => {
            var advMenu = new GenericMenu();
            if (savedProp != null) {
                advMenu.AddItem(new GUIContent("Saved Between Worlds"), savedProp.boolValue, () => {
                    savedProp.boolValue = !savedProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (sliderProp != null) {
                advMenu.AddItem(new GUIContent("Use Slider Wheel"), sliderProp.boolValue, () => {
                    sliderProp.boolValue = !sliderProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (securityEnabledProp != null) {
                advMenu.AddItem(new GUIContent("Protect with Security"), securityEnabledProp.boolValue, () => {
                    securityEnabledProp.boolValue = !securityEnabledProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (defaultOnProp != null) {
                advMenu.AddItem(new GUIContent("Default On"), defaultOnProp.boolValue, () => {
                    defaultOnProp.boolValue = !defaultOnProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (includeInRestProp != null) {
                advMenu.AddItem(new GUIContent("Show in Rest Pose"), includeInRestProp.boolValue, () => {
                    includeInRestProp.boolValue = !includeInRestProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (resetPhysboneProp != null) {
                advMenu.AddItem(new GUIContent("Add PhysBone to Reset"), false, () => {
                    VRCFuryEditorUtils.AddToList(resetPhysboneProp);
                });
            }

            if (enableExclusiveTagProp != null) {
                advMenu.AddItem(new GUIContent("Enable Exclusive Tags"), enableExclusiveTagProp.boolValue, () => {
                    enableExclusiveTagProp.boolValue = !enableExclusiveTagProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (exclusiveOffStateProp != null) {
                advMenu.AddItem(new GUIContent("This is Exclusive Off State"), exclusiveOffStateProp.boolValue, () => {
                    exclusiveOffStateProp.boolValue = !exclusiveOffStateProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (enableIconProp != null) {
                advMenu.AddItem(new GUIContent("Set Custom Menu Icon"), enableIconProp.boolValue, () => {
                    enableIconProp.boolValue = !enableIconProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (enableDriveGlobalParamProp != null) {
                advMenu.AddItem(new GUIContent("Drive a Global Parameter"), enableDriveGlobalParamProp.boolValue, () => {
                    enableDriveGlobalParamProp.boolValue = !enableDriveGlobalParamProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (separateLocalProp != null)
            {
                advMenu.AddItem(new GUIContent("Separate Local State"), separateLocalProp.boolValue, () => {
                    separateLocalProp.boolValue = !separateLocalProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            if (hasTransitionProp != null)
            {
                advMenu.AddItem(new GUIContent("Enable Transition State"), hasTransitionProp.boolValue, () => {
                    hasTransitionProp.boolValue = !hasTransitionProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            advMenu.AddItem(new GUIContent("Has Transition Time"), hasTransitionTimeProp.boolValue, () => {
                    hasTransitionTimeProp.boolValue = !hasTransitionTimeProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            advMenu.AddItem(new GUIContent("Run Animation to Completion"), hasExitTimeProp.boolValue, () => {
                    hasExitTimeProp.boolValue = !hasExitTimeProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });

            if (holdButtonProp != null) {
                advMenu.AddItem(new GUIContent("Hold Button"), holdButtonProp.boolValue, () => {
                    holdButtonProp.boolValue = !holdButtonProp.boolValue;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            advMenu.ShowAsContext();
        });
        button.style.flexGrow = 0;
        flex.Add(button);

        renderBody(content);

        if (resetPhysboneProp != null) {
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (resetPhysboneProp.arraySize > 0) {
                    c.Add(VRCFuryEditorUtils.WrappedLabel("Reset PhysBones:"));
                    c.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("resetPhysbones")));
                }
                return c;
            }, resetPhysboneProp));
        }

        if (enableExclusiveTagProp != null) {
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (enableExclusiveTagProp.boolValue) {
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("exclusiveTag"), "Exclusive Tags"));
                }
                return c;
            }, enableExclusiveTagProp));
        }

        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var c = new VisualElement();
            if (sliderProp.boolValue && defaultOnProp.boolValue) {
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("defaultSliderValue"), "Default Value"));
            }
            return c;
        }, sliderProp, defaultOnProp));

        if (enableIconProp != null) {
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (enableIconProp.boolValue) {
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("icon"), "Menu Icon"));
                }
                return c;
            }, enableIconProp));
        }

        if (enableDriveGlobalParamProp != null) {
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (enableDriveGlobalParamProp.boolValue) {
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("driveGlobalParam"), "Drive Global Param"));
                    c.Add(VRCFuryEditorUtils.Warn(
                        "Warning, Drive Global Param is an advanced feature. The driven parameter should not be placed in a menu " +
                        "or controlled by any other driver or shared with any other toggle. It should only be used as an input to " +
                        "manually-created state transitions in your avatar. This should NEVER be used on vrcfury props, as any merged " +
                        "full controllers will have their parameters rewritten."));
                }
                return c;
            }, enableDriveGlobalParamProp));
        }

        if (separateLocalProp != null)
        {
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var c = new VisualElement();
                if (separateLocalProp.boolValue)
                {
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("localState"), "Local State"));
                }
                return c;
            }, separateLocalProp));
        }

        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var c = new VisualElement();
            if (hasTransitionProp.boolValue)
            {
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("transitionStateIn"), "Transition In"));

                if (!simpleOutTransitionProp.boolValue)
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("transitionStateOut"), "Transition Out"));
            }
            return c;
        }, hasTransitionProp, simpleOutTransitionProp));

        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var c = new VisualElement();
            if (separateLocalProp.boolValue && hasTransitionProp.boolValue)
            {
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("localTransitionStateIn"), "Local Trans. In"));

                if (!simpleOutTransitionProp.boolValue)
                    c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("localTransitionStateOut"), "Local Trans. Out"));

            }
            return c;
        }, separateLocalProp, hasTransitionProp, simpleOutTransitionProp));

        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var c = new VisualElement();
            if (hasTransitionProp.boolValue)
            {
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("simpleOutTransition"), "Transition Out is reverse of Transition In"));
            }
            return c;
        }, hasTransitionProp));

        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
            var c = new VisualElement();
            if (hasTransitionTimeProp.boolValue)
            {
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("transitionTime"), "Transition Time"));
            }
            return c;
        }, hasTransitionTimeProp));

        // Tags
        content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var tags = new List<string>();
                if (savedProp != null && savedProp.boolValue)
                    tags.Add("Saved");
                if (sliderProp != null && sliderProp.boolValue)
                    tags.Add("Slider");
                if (securityEnabledProp != null && securityEnabledProp.boolValue)
                    tags.Add("Security");
                if (defaultOnProp != null && defaultOnProp.boolValue)
                    tags.Add("Default On");
                if (includeInRestProp != null && includeInRestProp.boolValue)
                    tags.Add("Shown in Rest Pose");
                if (exclusiveOffStateProp != null && exclusiveOffStateProp.boolValue)
                    tags.Add("This is the Exclusive Off State");
                if (holdButtonProp != null && holdButtonProp.boolValue)
                    tags.Add("Hold Button");
                if (hasExitTimeProp != null && hasExitTimeProp.boolValue)
                    tags.Add("Run to Completion");

                var row = new VisualElement();
                row.style.flexWrap = Wrap.Wrap;
                row.style.flexDirection = FlexDirection.Row;
                foreach (var tag in tags) {
                    var flag = new Label(tag);
                    flag.style.width = StyleKeyword.Auto;
                    flag.style.backgroundColor = new Color(1f, 1f, 1f, 0.1f);
                    flag.style.borderTopRightRadius = 5;
                    flag.style.marginRight = 5;
                    VRCFuryEditorUtils.Padding(flag, 2, 4);
                    row.Add(flag);
                }

                return row;
            },
            savedProp,
            sliderProp,
            securityEnabledProp,
            defaultOnProp,
            includeInRestProp,
            exclusiveOffStateProp,
            holdButtonProp,
            hasExitTimeProp
        ));

        return content;
    }
}

}


