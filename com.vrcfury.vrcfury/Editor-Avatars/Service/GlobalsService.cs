using System;
using System.Collections.Generic;
using VF.Feature.Base;
using VF.Model.Feature;
using VF.Utils.Controller;
using System.Linq;
using VF.Utils;

namespace VF.Service {
    /**
     * Holds things that are otherwise hard to autowire
     */
    internal class GlobalsService {
        public VFGameObject avatarObject;
        public Action<FeatureModel> addOtherFeature;
        public List<FeatureModel> allFeaturesInRun;
        public List<FeatureBuilder> allBuildersInRun;
        public int currentFeatureNum = 0;
        public string currentFeatureName = "";
        public string currentFeatureClipPrefix = "";
        public int currentMenuSortPosition = 0;
        public VFAFloat currentTriggerParam;
        public FeatureBuilder currentFeature;
        public string currentFeatureObjectPath = "";

        public bool IsFirst(FeatureBuilder builder) {
            var first = allBuildersInRun.FirstOrDefault(b => b.GetType() == builder.GetType());
            return first != null && first == builder;
        }
    }
}
