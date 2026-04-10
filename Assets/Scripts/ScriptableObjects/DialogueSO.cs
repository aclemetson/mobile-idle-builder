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
