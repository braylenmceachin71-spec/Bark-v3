using System;
using System.Collections.Generic;
using Bark.GUI;
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
    public static bool active;

    // Networking
    public static readonly string playerSizeKey = "BarkPlayerSize";
    public static Dictionary<VRRig, SizeChanger> sizeChangers = new();

    public static ConfigEntry<bool> ShowNetworkedSizes;

    // Size settings
    private const float LeftPrimaryIncrease = 0.05f;
    private const float RightPrimaryIncrease = 0.15f;

    private const float MinSize = 0.03f;
    private const float MaxSize = 20f;

    private float cachedSize;

    private bool leftPrimaryWasPressed;
    private bool rightPrimaryWasPressed;

    private void Awake()
    {
        try
        {
            Instance = this;

            VRRigCachePatches.OnRigCached += OnRigCached;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                GTPlayer.Instance.scale
            );
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
            if (GTPlayer.Instance == null)
                return;

            float currentSize = GTPlayer.Instance.scale;

            if (Mathf.Approximately(cachedSize, currentSize))
                return;

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                currentSize
            );

            cachedSize = currentSize;
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

            // Left Primary = smaller growth
            if (leftPrimaryPressed && !leftPrimaryWasPressed)
            {
                ChangeSize(LeftPrimaryIncrease);
            }

            // Right Primary = bigger growth
            if (rightPrimaryPressed && !rightPrimaryWasPressed)
            {
                ChangeSize(RightPrimaryIncrease);
            }

            leftPrimaryWasPressed = leftPrimaryPressed;
            rightPrimaryWasPressed = rightPrimaryPressed;
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
            if (sizeChanger != null)
                sizeChanger.gameObject.Obliterate();

            /*
             * The original potion version initialized the SizeChanger
             * using the square root of the current player scale.
             *
             * We keep that behavior so the existing Bark SizeChanger
             * implementation receives the same type of value.
             */
            float scale = (float)Math.Sqrt(GTPlayer.Instance.scale);

            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                GTPlayer.Instance.scale
            );

            sizeChanger =
                new GameObject("Bark Size Changer")
                    .AddComponent<SizeChanger>();

            sizeChangerTraverse = Traverse.Create(sizeChanger);

            minScale =
                sizeChangerTraverse.Field("minScale");

            maxScale =
                sizeChangerTraverse.Field("maxScale");

            sizeChangerTraverse
                .Field("myType")
                .SetValue(SizeChanger.ChangerType.Static);

            sizeChangerTraverse
                .Field("staticEasing")
                .SetValue(.5f);

            minScale.SetValue(scale);
            maxScale.SetValue(scale);

            cachedSize = GTPlayer.Instance.scale;

            leftPrimaryWasPressed = false;
            rightPrimaryWasPressed = false;

            active = true;
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
            if (sizeChanger == null)
                return;

            /*
             * SizeChanger.MinScale is the value currently being used
             * by the SizeChanger.
             *
             * Add the requested amount and clamp it between the
             * allowed minimum and maximum.
             */
            float newSize =
                Mathf.Clamp(
                    sizeChanger.MinScale + amount,
                    MinSize,
                    MaxSize
                );

            minScale.SetValue(newSize);
            maxScale.SetValue(newSize);

            active = true;

            /*
             * Immediately queue the new size for networking instead
             * of waiting for FixedUpdate.
             */
            NetworkPropertyHandler.Instance?.ChangeProperty(
                playerSizeKey,
                newSize
            );

            cachedSize = newSize;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
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
            base.OnDestroy();

            /*
             * Reset everyone back to normal when the module is
             * disabled/destroyed.
             */
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
                    var managerTraverse =
                        Traverse.Create(manager);

                    var scaleFromChanger =
                        managerTraverse.Method("ScaleFromChanger");

                    var controllingChanger =
                        managerTraverse.Method("ControllingChanger");

                    if (manager.myType !=
                        SizeManager.SizeChangerType.LocalOffline)
                    {
                        var target =
                            manager.targetRig?.transform;

                        if (!target)
                            continue;

                        var changer =
                            controllingChanger
                                .GetValue<SizeChanger>(target);

                        if (changer == null)
                            continue;

                        float scale =
                            scaleFromChanger
                                .GetValue<float>(changer, target);

                        target.localScale =
                            Vector3.one * scale;

                        manager.targetRig.ScaleMultiplier =
                            scale;
                    }
                    else
                    {
                        var cameraTransform =
                            manager.mainCameraTransform;

                        var player =
                            manager.targetPlayer;

                        var changer =
                            controllingChanger
                                .GetValue<SizeChanger>(
                                    cameraTransform
                                );

                        if (changer == null)
                            continue;

                        float scale =
                            scaleFromChanger
                                .GetValue<float>(
                                    changer,
                                    cameraTransform
                                );

                        player.turnParent
                            .transform
                            .localScale =
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
    }

    protected override void Cleanup()
    {
        try
        {
            active = false;

            leftPrimaryWasPressed = false;
            rightPrimaryWasPressed = false;

            /*
             * Remove our local SizeChanger.
             */
            sizeChanger?.gameObject?.Obliterate();

            sizeChanger = null;
            sizeChangerTraverse = null;
            minScale = null;
            maxScale = null;
        }
        catch (Exception e)
        {
            Logging.Exception(e);
        }
    }

    public override string GetDisplayName()
    {
        return DisplayName;
    }

    public override string Tutorial()
    {
        return string.Format(
            "- Left Primary: Grow +0.05x\n" +
            "- Right Primary: Grow +0.15x\n\n" +
            "Maximum size: 20x\n" +
            "Current size: {0:0.##}x",
            GTPlayer.Instance.scale
        );
    }

    protected override void ReloadConfiguration()
    {
        // No local potion models to configure.
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

    /*
     * This is the existing networked-player support from the
     * original Potions module.
     *
     * Other players who have BarkPlayerSize will get a SizeChanger
     * that follows their networked scale.
     */
    public static void TryGetSizeChangerForRig(
        VRRig rig,
        out SizeChanger sc
    )
    {
        sc = null;

        try
        {
            if (rig == null ||
                rig.OwningNetPlayer == null)
                return;

            if (!rig.HasProperty(playerSizeKey))
                return;

            float size =
                rig.OwningNetPlayer
                    .GetProperty<float>(playerSizeKey);

            if (sizeChangers.ContainsKey(rig))
            {
                sc = sizeChangers[rig];

                if (sc == null)
                {
                    sizeChangers.Remove(rig);
                    sc = null;
                    return;
                }

                var sizeChangerTraverse =
                    Traverse.Create(sc);

                var remoteMinScale =
                    sizeChangerTraverse.Field("minScale");

                var remoteMaxScale =
                    sizeChangerTraverse.Field("maxScale");

                /*
                 * Smooth the remote player's size so their
                 * model doesn't snap instantly.
                 */
                size =
                    Mathf.Lerp(
                        sc.MinScale,
                        size,
                        .75f * Time.fixedDeltaTime
                    );

                remoteMinScale.SetValue(size);
                remoteMaxScale.SetValue(size);
            }
            else
            {
                size =
                    Mathf.Lerp(
                        rig.scaleFactor,
                        size,
                        .75f * Time.fixedDeltaTime
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
        var newSizeChanger =
            new GameObject("Bark Size Changer")
                .AddComponent<SizeChanger>();

        var traverse =
            Traverse.Create(newSizeChanger);

        var newMinScale =
            traverse.Field("minScale");

        var newMaxScale =
            traverse.Field("maxScale");

        traverse
            .Field("myType")
            .SetValue(SizeChanger.ChangerType.Static);

        traverse
            .Field("staticEasing")
            .SetValue(.5f);

        newMinScale.SetValue(scale);
        newMaxScale.SetValue(scale);

        return newSizeChanger;
    }
}
