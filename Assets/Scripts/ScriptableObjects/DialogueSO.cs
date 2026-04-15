using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    [Serializable]
    public struct DialogueLine
    {
        public string speakerName;
        [TextArea(2, 4)]
        public string text;
        public Sprite portrait;
        /// <summary>Freeze Time.timeScale while this line is displayed.</summary>
        public bool   pauseGame;
        /// <summary>
        /// ID of a UI element or world object to highlight while this line is shown.
        /// Examples: "electron_field", "quark_field", "maxwells_demon", "btn-research".
        /// Empty string = no highlight / clear previous.
        /// </summary>
        public string highlightTarget;
        /// <summary>
        /// Optional one-shot directive fired when this line is shown.
        /// Examples: "pulse_field", "pulse_building", "zoom_field".
        /// Routed to TutorialOverlayController via DialogueController.OnActionTriggered.
        /// </summary>
        public string action;
    }

    /// <summary>
    /// Ordered sequence of dialogue lines spoken by one or more characters.
    /// Used by DialogueController to play through narration or tutorial prompts.
    /// </summary>
    [CreateAssetMenu(fileName = "New Dialogue", menuName = "MobileIdleBuilder/Dialogue")]
    public class DialogueSO : ScriptableObject
    {
        public DialogueLine[] lines;
    }
}
