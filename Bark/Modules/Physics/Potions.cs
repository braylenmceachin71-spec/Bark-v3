using System;
using System.Collections.Generic;
using Bark.Extensions;
using Bark.GUI;
using Bark.Helpers;
using Bark.Networking;
using Bark.Patches;
using Bark.Tools;
using BepInEx.Configuration;
using GorillaLocomotion;
using HarmonyLib;
using UnityEngine;

namespace Bark.Modules.Physics;

public class Potions : BarkModule
{
    public static readonly string DisplayName = "Size Changer";

    public static SizeChanger sizeChanger;
    public static Traverse sizeChangerTraverse, minScale, maxScale;
    public static Potions Instance;

    public static readonly string playerSizeKey = "BarkPlayerSize";
    public static Dictionary<VRRig, SizeChanger> sizeChangers = new();

    public static ConfigEntry<bool> ShowNetworkedSizes;

    private const float MinSize = 0.03f;
    private const float MaxSize = 20f;

    // How much each button press changes your size.
    private const float LeftPrimaryChange = -0.05f;
    private const float RightPrimaryChange = 0.15f;

    private float cachedSize;

    private bool leftPrimaryWasPressed;
    private bool rightPrimaryWasPressed;

    private void Awake()
    {
        try
        {
            Instance = this;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                GTPlayer.Instance.scale
            );

            VRRigCachePatches.OnRigCached += OnRigCached;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    private void FixedUpdate()
    {
        try
        {
            if (Mathf.Approximately(cachedSize, GTPlayer.Instance.scale))
                return;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                GTPlayer.Instance.scale
            );

            cachedSize = GTPlayer.Instance.scale;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    private void Update()
    {
        try
        {
            if (!active || sizeChanger == null)
                return;

            bool leftPrimaryPressed =
                ControllerInputPoller.instance.leftControllerPrimaryButton;

            bool rightPrimaryPressed =
                ControllerInputPoller.instance.rightControllerPrimaryButton;

            // Left Primary = smaller
            if (leftPrimaryPressed && !leftPrimaryWasPressed)
            {
                ChangeSize(LeftPrimaryChange);
            }

            // Right Primary = bigger
            if (rightPrimaryPressed && !rightPrimaryWasPressed)
            {
                ChangeSize(RightPrimaryChange);
            }

            leftPrimaryWasPressed = leftPrimaryPressed;
            rightPrimaryWasPressed = rightPrimaryPressed;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    private void ChangeSize(float amount)
    {
        try
        {
            float currentSize = GTPlayer.Instance.scale;

            float newSize = Mathf.Clamp(
                currentSize + amount,
                MinSize,
                MaxSize
            );

            minScale.SetValue(newSize);
            maxScale.SetValue(newSize);

            cachedSize = newSize;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                newSize
            );
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    protected override void OnEnable()
    {
        try
        {
            if (!MenuController.Instance.Built)
                return;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                GTPlayer.Instance.scale
            );

            base.OnEnable();

            active = false;

            Setup();
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    private void Setup()
    {
        try
        {
            float scale = Mathf.Clamp(
                GTPlayer.Instance.scale,
                MinSize,
                MaxSize
            );

            sizeChanger = new GameObject("Bark Size Changer")
                .AddComponent<SizeChanger>();

            sizeChangerTraverse = Traverse.Create(sizeChanger);

            minScale = sizeChangerTraverse.Field("minScale");
            maxScale = sizeChangerTraverse.Field("maxScale");

            sizeChangerTraverse
                .Field("myType")
                .SetValue(SizeChanger.ChangerType.Static);

            sizeChangerTraverse
                .Field("staticEasing")
                .SetValue(0.5f);

            minScale.SetValue(scale);
            maxScale.SetValue(scale);

            cachedSize = scale;

            ReloadConfiguration();
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    protected override void Cleanup()
    {
        try
        {
            active = false;

            leftPrimaryWasPressed = false;
            rightPrimaryWasPressed = false;

            sizeChanger?.gameObject.Obliterate();
            sizeChanger = null;

            cachedSize = GTPlayer.Instance.scale;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    protected override void ReloadConfiguration()
    {
        // No local models to configure.
    }

    public override string GetDisplayName()
    {
        return DisplayName;
    }

    public override string Tutorial()
    {
        return string.Format(
            "- Left Primary: Shrink by 0.05x\n" +
            "- Right Primary: Grow by 0.15x\n\n" +
            "Minimum size: 0.03x\n" +
            "Maximum size: 20x\n\n" +
            "Current size: {0:0.##}x",
            GTPlayer.Instance.scale
        );
    }

    public static void BindConfigEntries()
    {
        ShowNetworkedSizes = Plugin.ConfigFile.Bind(
            DisplayName,
            "show networked size",
            true,
            "Whether or not to show how big other players using the Size Changer module are"
        );
    }

    public static void TryGetSizeChangerForRig(
        VRRig rig,
        out SizeChanger sc
    )
    {
        try
        {
            if (!rig.HasProperty(playerSizeKey))
            {
                sc = null;
                return;
            }

            float size =
                rig.OwningNetPlayer.GetProperty<float>(playerSizeKey);

            if (sizeChangers.ContainsKey(rig))
            {
                sc = sizeChangers[rig];

                var remoteTraverse = Traverse.Create(sc);
                var remoteMinScale = remoteTraverse.Field("minScale");
                var remoteMaxScale = remoteTraverse.Field("maxScale");

                size = Mathf.Lerp(
                    sc.MinScale,
                    size,
                    0.75f * Time.fixedDeltaTime
                );

                remoteMinScale.SetValue(size);
                remoteMaxScale.SetValue(size);
            }
            else
            {
                size = Mathf.Lerp(
                    rig.scaleFactor,
                    size,
                    0.75f * Time.fixedDeltaTime
                );

                sc = CreateSizeChanger(size);
                sizeChangers.Add(rig, sc);
            }
        }
        catch (Exception e)
        {
            Logging.Exception(e);
            sc = null;
        }
    }

    public static SizeChanger CreateSizeChanger(float scale)
    {
        var changer = new GameObject("Bark Size Changer")
            .AddComponent<SizeChanger>();

        var changerTraverse = Traverse.Create(changer);

        var changerMinScale = changerTraverse.Field("minScale");
        var changerMaxScale = changerTraverse.Field("maxScale");

        changerTraverse
            .Field("myType")
            .SetValue(SizeChanger.ChangerType.Static);

        changerTraverse
            .Field("staticEasing")
            .SetValue(0.5f);

        changerMinScale.SetValue(scale);
        changerMaxScale.SetValue(scale);

        return changer;
    }

    private void OnRigCached(NetPlayer player, VRRig rig)
    {
        try
        {
            rig.transform.localScale = Vector3.one;
            rig.ScaleMultiplier = 1;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    protected override void OnDestroy()
    {
        try
        {
            VRRigCachePatches.OnRigCached -= OnRigCached;

            foreach (var rig in RigHelper.GetActiveRigs())
            {
                try
                {
                    rig.transform.localScale = Vector3.one;
                    rig.ScaleMultiplier = 1;
                }
                catch (Exception e)
                {
                    Logging.Exception(e);
                }
            }

            foreach (var manager in FindObjectsOfType<SizeManager>())
            {
                try
                {
                    var managerTraverse = Traverse.Create(manager);

                    var scaleFromChanger =
                        managerTraverse.Method("ScaleFromChanger");

                    var controllingChanger =
                        managerTraverse.Method("ControllingChanger");

                    if (manager.myType != SizeManager.SizeChangerType.LocalOffline)
                    {
                        var target = manager.targetRig?.transform;

                        if (!target)
                            continue;

                        var changer =
                            controllingChanger.GetValue<SizeChanger>(target);

                        var scale =
                            scaleFromChanger.GetValue<float>(
                                changer,
                                target
                            );

                        target.localScale =
                            Vector3.one * scale;

                        manager.targetRig.ScaleMultiplier = scale;
                    }
                    else
                    {
                        var target = manager.mainCameraTransform;
                        var player = manager.targetPlayer;

                        var changer =
                            controllingChanger.GetValue<SizeChanger>(target);

                        var scale =
                            scaleFromChanger.GetValue<float>(
                                changer,
                                target
                            );

                        player.turnParent.transform.localScale =
                            Vector3.one * scale;

                        player.SetScaleMultiplier(scale);
                    }
                }
                catch (Exception e)
                {
                    Logging.Exception(e);
                }
            }

            sizeChangers.Clear();
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }

        base.OnDestroy();
    }
}
