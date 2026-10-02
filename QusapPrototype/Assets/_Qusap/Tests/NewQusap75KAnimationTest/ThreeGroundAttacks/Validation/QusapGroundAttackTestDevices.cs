#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// Batch regression tests expect a keyboard device. A graphical desktop has
// one, while this batch editor has none. Supply a fixture only for -runTests.
public static class QusapGroundAttackTestDevices
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnsureBatchKeyboard()
    {
        if(!Environment.GetCommandLineArgs().Any(a=>a=="-runTests")) return;
        // A batch editor has no focused Game view. Use an in-memory settings
        // clone so desktop focus cannot discard fixture key events; the project's
        // InputSettings asset and its bindings remain byte-for-byte unchanged.
        var settings=UnityEngine.Object.Instantiate(InputSystem.settings);
        settings.hideFlags=HideFlags.HideAndDontSave;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings=settings;
        if(Keyboard.current==null) InputSystem.AddDevice<Keyboard>("BatchRegressionKeyboard");
    }
}
#endif
