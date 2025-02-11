using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VF.Builder;
using VF.Feature;
using VF.Feature.Base;
using VF.Injector;
using VF.Utils;
using VF.Utils.Controller;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using static VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

namespace VF.Service {
    [VFService]
    internal class FloatToDriverService {
        [VFAutowired] private readonly GlobalsService globals;
        [VFAutowired] private readonly ControllersService controllers;
        [VFAutowired] private readonly DbtLayerService dbtLayerService;
        private ControllerManager fx => controllers.GetFx();
        [VFAutowired] private readonly MenuService menuService;
        private MenuManager menu => menuService.GetMenu();
        private VFLayer layer;
        private VFState idle;

        private BlendtreeMath math;
        private readonly Dictionary<string, VFLayer> driveLayers = new Dictionary<string, VFLayer>();

        private readonly Dictionary<string, (VFAInteger,int,VFTransition)> currentSettings = new Dictionary<string, (VFAInteger,int,VFTransition)>();
        private readonly Dictionary<VFAFloat, VFState> createdStates = new Dictionary<VFAFloat, VFState>();

        private readonly List<(VFAFloat,string,float)> drivenSyncParams = new List<(VFAFloat,string,float)>();
        private readonly List<(VFAFloat,string,float)> drivenToggles = new List<(VFAFloat,string,float)>();
        private readonly List<(VFAFloat,string,float,FeatureBuilder)> drivenTags = new List<(VFAFloat,string,float,FeatureBuilder)>();

        public void DriveSyncParam(VFAFloat src, string param, float value) {
            drivenSyncParams.Add((src, param, value));
        }

        public void DriveToggle(VFAFloat src, string toggle, float value) {
            drivenToggles.Add((src, toggle, value));
        }

        public void DriveTag(VFAFloat src, string tag, float value, FeatureBuilder originFeature) {
            drivenTags.Add((src, tag, value, originFeature));
        }

        public VFAFloat Drive(string output, float? onValue, float? offValue, VFAFloat control = null) {
            if (math == null) {
                var dbt = dbtLayerService.Create("FloatToDriverService");
                math = dbtLayerService.GetMath(dbt);
            }
            if (control == null) {
                control = fx.NewFloat($"Drive {output} to {onValue}/{offValue}");
            }
            var buffer = math.Buffer(control);

            if (!driveLayers.TryGetValue(output, out var layer)) {
                layer = fx.NewLayer($"FloatToDriverService - {output}");
                layer.NewState("Idle");
                layer.SetNextOffset(1, 0);
                driveLayers[output] = layer;
            }

            void MakeLastAnyTransitionFirst() {
                var oldAny = layer.GetRawStateMachine().anyStateTransitions;
                layer.GetRawStateMachine().anyStateTransitions = new[] { oldAny.Last() }
                    .Concat(oldAny.Take(oldAny.Length - 1))
                    .ToArray();
            }

            if (offValue.HasValue) {
                var state = layer.NewState($"Set to {offValue}");
                state.Drives(output, offValue.Value);
                state.TransitionsFromAny().When(control.IsLessThanOrEquals(0).And(buffer.IsGreaterThan(0)));
                MakeLastAnyTransitionFirst();
            }
            if (onValue.HasValue) {
                var state = layer.NewState($"Set to {onValue}");
                state.Drives(output, onValue.Value);
                state.TransitionsFromAny().When(control.IsGreaterThan(0).And(buffer.IsLessThanOrEquals(0)));
                MakeLastAnyTransitionFirst();
            }

            return control;
        }
        
        private readonly List<(VFAFloat,string,float)> drivenParams = new List<(VFAFloat,string,float)>();

        public void DriveAutoLater(VFAFloat input, string output, float value) {
            drivenParams.Add((input, output, value));
        }

        [FeatureBuilderAction(FeatureOrder.DriveNonFloatTypes)]
        public void DriveNonFloatTypes() {
            var nonFloatParams = new HashSet<string>();
            foreach (var c in controllers.GetAllUsedControllers()) {
                nonFloatParams.UnionWith(c.GetRaw().parameters
                    .Where(p => p.type != AnimatorControllerParameterType.Float || c.GetType() != VRCAvatarDescriptor.AnimLayerType.FX)
                    .Select(p => p.name));
            }

            List<(VFAFloat, string, float)> triggers = new();
            foreach (var trigger in drivenTags) {
                var (param, tag, target, feature) = trigger;
                foreach (var other in globals.allBuildersInRun
                     .OfType<ToggleBuilder>()
                     .Where(b => b != feature)) {
                        var otherTags = other.GetTags();
                        
                        if (otherTags.Contains(tag)) {
                            if (target == 0) triggers.Add((param, other.getParam(), 0));
                            else triggers.Add((param, other.getParam(), other.model.slider ? target : 1));
                        }
                }
            }

            foreach (var trigger in drivenToggles) {
                var (param, path, target) = trigger;
                var control = menu.GetMenuItem(path);
                if (control == null) continue;
                if (target == 0) triggers.Add((param, control.parameter.name, 0));
                else if (control.type == ControlType.RadialPuppet) triggers.Add((param, control.parameter.name, target));
                else triggers.Add((param, control.parameter.name, control.value));
            }

            foreach (var trigger in drivenSyncParams) {
                var (triggerParam, param, target) = trigger;
                triggers.Add((triggerParam, param, target));
            }

            foreach (var trigger in triggers) {
                var (triggerParam, param, value) = trigger;
                Drive(param, value, null, triggerParam);
            }

        }
    }
}
