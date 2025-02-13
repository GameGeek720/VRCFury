using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VF.Builder;
using VF.Feature.Base;
using VF.Injector;
using VF.Utils;
using VF.Utils.Controller;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace VF.Service {
    [VFService]
    internal class FloatToDriverService {
        [VFAutowired] private readonly ControllersService controllers;
        [VFAutowired] private readonly DbtLayerService dbtLayerService;
        private ControllerManager fx => controllers.GetFx();

        private BlendtreeMath math;
        private readonly List<DriveRequest> driveRequests = new List<DriveRequest>();

        private class DriveRequest {
            public string output;
            public VFAFloat control;
            public float? onValue;
            public float? offValue;
        }

        [FeatureBuilderAction(FeatureOrder.DriveNonFloatTypes)]
        public void Apply() {
            if (driveRequests.Count == 0) return;
            
            var dbt = dbtLayerService.Create("FloatToDriverService - DBT");
            var math = dbtLayerService.GetMath(dbt);

            var layer = fx.NewLayer($"FloatToDriverService - Driver");
            layer.NewState("Idle");
            layer.SetNextOffset(1, 0);
            
            var threshold = 0.003f;

            foreach (var outputGroup in driveRequests.GroupBy(req => req.output)) {
                var output = outputGroup.Key;
                var resolvedTrigger = fx.MakeAap($"FloatToDriverService - {output} (Resolved)");
                var resolvedTriggerLastValue = 0;
                var conditions = new List<(Motion, BlendtreeMath.VFAFloatBool)>();
                var drivenLastFrame = fx.MakeAap($"FloatToDriverService - {output} (Driven Last Frame)");
                var drivenLastFrameClip = VrcfObjectFactory.Create<AnimationClip>();
                drivenLastFrameClip.SetAap(drivenLastFrame, 1);
                var changedLastFrame = fx.MakeAap($"FloatToDriverService - {output} (Changed Last Frame)");
                
                void AddTrigger(float targetValue, BlendtreeMath.VFAFloatBool triggeredCondition) {
                    var triggered = math.SetValueWithConditions($"FloatToDriverService - {output} = {targetValue} (Trigger)",
                        (1f, triggeredCondition),
                        (0f, null)
                    );
                    var resolvedTriggerValue = ++resolvedTriggerLastValue;
                    var clip = VrcfObjectFactory.Create<AnimationClip>();
                    clip.SetAap(changedLastFrame, 1);
                    clip.SetAap(resolvedTrigger, resolvedTriggerValue);
                    conditions.Add((clip, BlendtreeMath.GreaterThan(triggered, threshold)));

                    var state = layer.NewState($"{output} = {targetValue}");
                    state.TransitionsFromAny().When(resolvedTrigger.AsFloat().IsGreaterThan(resolvedTriggerValue - threshold)
                        .And(resolvedTrigger.AsFloat().IsLessThan(resolvedTriggerValue + threshold)));
                    state.Drives(output, targetValue);
                    state.WithAnimation(drivenLastFrameClip);
                }
                
                foreach (var req in outputGroup.Reverse()) {
                    var input = req.control;
                    var inputBuffered = math.Buffer(input);
                    if (req.offValue.HasValue) {
                        AddTrigger(req.offValue.Value, BlendtreeMath.GreaterThan(inputBuffered, threshold, true)
                            .And(BlendtreeMath.LessThan(input, threshold)));
                    }
                    if (req.onValue.HasValue) {
                        AddTrigger(req.onValue.Value, BlendtreeMath.LessThan(inputBuffered, threshold)
                            .And(BlendtreeMath.GreaterThan(input, threshold, true)));
                    }
                }

                var maintainClip = resolvedTrigger.MakeCopier(resolvedTrigger);
                conditions.Add((null, BlendtreeMath.GreaterThan(changedLastFrame, threshold).Or(BlendtreeMath.LessThan(drivenLastFrame, threshold))));
                conditions.Add((maintainClip, null));
                math.SetValueWithConditions(conditions.ToArray());
            }
        }

        public VFAFloat Drive(string output, float? onValue, float? offValue) {
            var control = fx.NewFloat($"Drive {output} to {onValue}/{offValue}");
            driveRequests.Add(new DriveRequest() {
                output = output,
                control = control,
                onValue = onValue,
                offValue = offValue
            });
            return control;
        }
    }
}
