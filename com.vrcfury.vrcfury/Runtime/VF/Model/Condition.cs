using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VF.Component;

namespace VF.Model.Conditions {
    [Serializable]
    public class Condition {
        [SerializeReference] public List<AndCondition> orConditions = new List<AndCondition>();
    }

    [Serializable]
    public class AndCondition {
        [SerializeReference] public List<Trigger> andCondition = new List<Trigger>();
    }

    [Serializable]
    public class Trigger {
    }
    
    [Serializable]
    public class MenuTrigger : Trigger {
        public string menuPath;
    }

    [Serializable]
    public class GlobalTrigger: Trigger {
        public string boolName;
    }

    [Serializable]
    public class GestureTrigger: Trigger {
        public Hand hand;
        public HandSign sign;
        public HandSign comboSign;

        public enum Hand {
            EITHER,
            LEFT,
            RIGHT,
            COMBO
        }
        
        public enum HandSign {
            NEUTRAL,
            FIST,
            HANDOPEN,
            FINGERPOINT,
            VICTORY,
            ROCKNROLL,
            HANDGUN,
            THUMBSUP
        }
    }
    

}
