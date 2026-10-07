using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public static class RaftNameRegistry
{
    public const int MaxNameLength = 24;

    private static readonly Dictionary<int, string>
        Names =
            new Dictionary<int, string>();

    public static string GetName(
        int raftId)
    {
        string value;

        if (Names.TryGetValue(
                raftId,
                out value) &&
            !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return GetDefaultName(
            raftId
        );
    }

    public static string NormalizeName(
        string value,
        int raftId)
    {
        if (string.IsNullOrEmpty(value))
        {
            return GetDefaultName(
                raftId
            );
        }

        StringBuilder builder =
            new StringBuilder(
                MaxNameLength
            );

        for (int i = 0;
             i < value.Length &&
             builder.Length < MaxNameLength;
             i++)
        {
            char character =
                value[i];

            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        string result =
            builder.ToString().Trim();

        if (string.IsNullOrEmpty(result))
        {
            return GetDefaultName(
                raftId
            );
        }

        return result;
    }

    public static void SetLocalName(
        int raftId,
        string raftName,
        bool save)
    {
        Names[raftId] =
            NormalizeName(
                raftName,
                raftId
            );

        if (save &&
            Raft_Network.IsHost)
        {
            SaveCurrentWorld();
        }
    }

    public static bool IsValidRaftId(
        int raftId)
    {
        if (raftId == 0)
        {
            return MultiRaftRegistry.MainPivot != null;
        }

        return raftId > 0 &&
            MultiRaftRegistry.GetRoot(
                raftId
            ) != null;
    }

    public static void LoadCurrentWorld()
    {
        Names.Clear();

        if (!Raft_Network.IsHost)
        {
            return;
        }

        string path =
            GetSavePath();

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string[] lines =
                File.ReadAllLines(path);

            for (int i = 0;
                 i < lines.Length;
                 i++)
            {
                string line =
                    lines[i];

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                int separator =
                    line.IndexOf('|');

                if (separator <= 0)
                {
                    continue;
                }

                int raftId;

                if (!int.TryParse(
                        line.Substring(
                            0,
                            separator
                        ),
                        out raftId
                    ))
                {
                    continue;
                }

                string encoded =
                    line.Substring(
                        separator + 1
                    );

                string name =
                    Encoding.UTF8.GetString(
                        Convert.FromBase64String(
                            encoded
                        )
                    );

                Names[raftId] =
                    NormalizeName(
                        name,
                        raftId
                    );
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[RebuildableRaft] Failed to load raft names - " +
                exception.Message
            );
        }
    }

    public static void SaveCurrentWorld()
    {
        if (!Raft_Network.IsHost)
        {
            return;
        }

        try
        {
            string path =
                GetSavePath();

            string directory =
                Path.GetDirectoryName(path);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(
                    directory
                );
            }

            List<string> lines =
                new List<string>();

            foreach (KeyValuePair<int, string> pair
                in Names)
            {
                string normalized =
                    NormalizeName(
                        pair.Value,
                        pair.Key
                    );

                string encoded =
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            normalized
                        )
                    );

                lines.Add(
                    pair.Key.ToString() +
                    "|" +
                    encoded
                );
            }

            File.WriteAllLines(
                path,
                lines.ToArray()
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[RebuildableRaft] Failed to save raft names - " +
                exception.Message
            );
        }
    }

    public static void Reset()
    {
        Names.Clear();
    }

    private static string GetDefaultName(
        int raftId)
    {
        return raftId == 0
            ? "Main Raft"
            : "Raft " + raftId;
    }

    private static string GetSavePath()
    {
        string worldName =
            SaveAndLoad.CurrentGameFileName;

        if (string.IsNullOrEmpty(worldName))
        {
            worldName =
                "CurrentWorld";
        }

        char[] invalid =
            Path.GetInvalidFileNameChars();

        for (int i = 0;
             i < invalid.Length;
             i++)
        {
            worldName =
                worldName.Replace(
                    invalid[i],
                    '_'
                );
        }

        return Path.Combine(
            Application.persistentDataPath,
            "RebuildableRaft",
            "RaftNames",
            worldName + ".txt"
        );
    }
}

public static class RaftReceiverRadar
{
    private sealed class ReceiverState
    {
        public readonly Dictionary<int, Reciever_Dot>
            dots =
                new Dictionary<int, Reciever_Dot>();
    }

    private static readonly Dictionary<Reciever, ReceiverState>
        States =
            new Dictionary<Reciever, ReceiverState>();

    private static readonly FieldInfo DotPrefabField =
        AccessTools.Field(
            typeof(Reciever),
            "dotPrefab"
        );

    private static readonly FieldInfo DotParentField =
        AccessTools.Field(
            typeof(Reciever),
            "dotParent"
        );

    private static readonly FieldInfo RadarLengthField =
        AccessTools.Field(
            typeof(Reciever),
            "radarLength"
        );

    private static readonly FieldInfo RadarUIWidthField =
        AccessTools.Field(
            typeof(Reciever),
            "radarUIWidth"
        );

    private static readonly FieldInfo RadarSectionField =
        AccessTools.Field(
            typeof(Reciever),
            "radarSection"
        );

    private static readonly FieldInfo HasBeenPlacedField =
        AccessTools.Field(
            typeof(Reciever),
            "hasBeenPlaced"
        );

    private static readonly FieldInfo DotTextField =
        AccessTools.Field(
            typeof(Reciever_Dot),
            "dotNumberText"
        );

    public static int GetRaftId(
        Reciever reciever)
    {
        if (reciever == null)
        {
            return int.MinValue;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    reciever.transform
                );

        if (root != null)
        {
            return root.RaftId;
        }

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot != null &&
            (reciever.transform == mainPivot ||
             reciever.transform.IsChildOf(
                 mainPivot
             )))
        {
            return 0;
        }

        return int.MinValue;
    }

    public static void UpdateReceiver(
        Reciever reciever)
    {
        if (reciever == null)
        {
            return;
        }

        if (!IsPlacedReceiver(
                reciever))
        {
            ReceiverState previewState;

            if (States.TryGetValue(
                    reciever,
                    out previewState))
            {
                SetDotsActive(
                    previewState,
                    false
                );
            }

            return;
        }

        int ownRaftId =
            GetRaftId(reciever);

        ReceiverState state =
            GetState(reciever);

        if (state == null)
        {
            return;
        }

        GameObject radarSection =
            RadarSectionField != null
                ? RadarSectionField.GetValue(
                    reciever
                ) as GameObject
                : null;

        if (ownRaftId == int.MinValue ||
            radarSection == null ||
            !radarSection.activeInHierarchy)
        {
            SetDotsActive(
                state,
                false
            );

            return;
        }

        Reciever_Dot dotPrefab =
            DotPrefabField != null
                ? DotPrefabField.GetValue(
                    reciever
                ) as Reciever_Dot
                : null;

        RectTransform dotParent =
            DotParentField != null
                ? DotParentField.GetValue(
                    reciever
                ) as RectTransform
                : null;

        if (dotPrefab == null ||
            dotParent == null)
        {
            return;
        }

        Dictionary<int, Reciever>
            targets =
                CollectTargetReceivers(
                    ownRaftId
                );

        HashSet<int> alive =
            new HashSet<int>();

        foreach (KeyValuePair<int, Reciever> pair
            in targets)
        {
            int targetRaftId =
                pair.Key;

            Reciever target =
                pair.Value;

            if (target == null)
            {
                continue;
            }

            Reciever_Dot dot;

            if (!state.dots.TryGetValue(
                    targetRaftId,
                    out dot) ||
                dot == null)
            {
                dot =
                    UnityEngine.Object.Instantiate<Reciever_Dot>(
                        dotPrefab,
                        dotParent
                    );

                dot.transform.localScale =
                    dotPrefab.transform.localScale;

                dot.chunkPoint = null;
                dot.SetTargetedByReciever(false);

                state.dots[targetRaftId] =
                    dot;
            }

            alive.Add(
                targetRaftId
            );

            UpdateDot(
                reciever,
                target,
                targetRaftId,
                dot
            );
        }

        List<int> remove =
            null;

        foreach (KeyValuePair<int, Reciever_Dot> pair
            in state.dots)
        {
            if (alive.Contains(pair.Key))
            {
                continue;
            }

            if (remove == null)
            {
                remove =
                    new List<int>();
            }

            remove.Add(pair.Key);
        }

        if (remove != null)
        {
            for (int i = 0;
                 i < remove.Count;
                 i++)
            {
                RemoveDot(
                    state,
                    remove[i]
                );
            }
        }
    }

    public static void RemoveReceiver(
        Reciever reciever)
    {
        if (reciever == null)
        {
            return;
        }

        ReceiverState state;

        if (!States.TryGetValue(
                reciever,
                out state))
        {
            return;
        }

        DestroyState(state);
        States.Remove(reciever);
    }

    public static void Reset()
    {
        foreach (ReceiverState state
            in States.Values)
        {
            DestroyState(state);
        }

        States.Clear();
    }

    private static ReceiverState GetState(
        Reciever reciever)
    {
        ReceiverState state;

        if (!States.TryGetValue(
                reciever,
                out state))
        {
            state =
                new ReceiverState();

            States[reciever] =
                state;
        }

        return state;
    }

    private static Dictionary<int, Reciever>
        CollectTargetReceivers(
            int ownRaftId)
    {
        Dictionary<int, Reciever> targets =
            new Dictionary<int, Reciever>();

        List<Reciever> recievers =
            Reciever.AllRecievers;

        if (recievers == null)
        {
            return targets;
        }

        for (int i = 0;
             i < recievers.Count;
             i++)
        {
            Reciever target =
                recievers[i];

            if (target == null ||
                !target.gameObject.activeInHierarchy)
            {
                continue;
            }

            int raftId =
                GetRaftId(target);

            if (raftId == int.MinValue ||
                raftId == ownRaftId ||
                targets.ContainsKey(raftId))
            {
                continue;
            }

            targets.Add(
                raftId,
                target
            );
        }

        return targets;
    }

    private static void UpdateDot(
        Reciever source,
        Reciever target,
        int targetRaftId,
        Reciever_Dot dot)
    {
        float radarLength =
            GetFloatField(
                RadarLengthField,
                source,
                50f
            );

        float radarUIWidth =
            GetFloatField(
                RadarUIWidthField,
                source,
                270f
            );

        Vector2 sourcePosition =
            new Vector2(
                source.transform.position.x,
                source.transform.position.z
            );

        Vector2 targetPosition =
            new Vector2(
                target.transform.position.x,
                target.transform.position.z
            );

        Vector2 direction =
            targetPosition -
            sourcePosition;

        float distance =
            direction.magnitude;

        float angle =
            Vector2.SignedAngle(
                Vector2.up,
                direction
            );

        if (angle < 0f)
        {
            angle =
                360f + angle;
        }

        angle =
            360f - angle;

        float rotation =
            360f -
            source.transform.eulerAngles.y +
            angle;

        Vector3 radarDirection =
            new Vector3(
                Mathf.Sin(
                    rotation * Mathf.Deg2Rad
                ),
                Mathf.Cos(
                    rotation * Mathf.Deg2Rad
                ),
                0f
            );

        radarDirection *=
            -1f;

        float normalizedDistance =
            Mathf.Clamp01(
                distance /
                Mathf.Max(
                    0.01f,
                    radarLength
                )
            );

        float radius =
            radarUIWidth * 0.5f;

        dot.gameObject.SetActive(true);

        Vector3 localPosition =
            radarDirection *
            radius *
            normalizedDistance;

        RectTransform dotRect =
            dot.transform as RectTransform;

        if (dotRect != null)
        {
            dotRect.localPosition =
                localPosition;
        }
        else
        {
            dot.transform.localPosition =
                localPosition;
        }

        dot.SetLengthToPoint(
            distance
        );

        dot.SetText(
            distance.ToString("0") +
            "m"
        );

        UpdateRaftNameLabel(
            dot,
            RaftNameRegistry.GetName(
                targetRaftId
            )
        );

        dot.SetTargetedByReciever(false);
        dot.transform.SetAsLastSibling();
    }

    private static bool IsPlacedReceiver(
        Reciever reciever)
    {
        if (reciever == null)
        {
            return false;
        }

        if (HasBeenPlacedField == null)
        {
            return Reciever.AllRecievers != null &&
                Reciever.AllRecievers.Contains(
                    reciever
                );
        }

        object value =
            HasBeenPlacedField.GetValue(
                reciever
            );

        return value is bool &&
            (bool)value;
    }

    private static void UpdateRaftNameLabel(
        Reciever_Dot dot,
        string raftName)
    {
        if (dot == null)
        {
            return;
        }

        Transform existing =
            dot.transform.Find(
                "RebuildableRaft_NameLabel"
            );

        GameObject labelObject;
        Text label;

        if (existing != null)
        {
            labelObject =
                existing.gameObject;

            label =
                labelObject.GetComponent<Text>();
        }
        else
        {
            labelObject =
                new GameObject(
                    "RebuildableRaft_NameLabel",
                    typeof(RectTransform),
                    typeof(Text),
                    typeof(Outline)
                );

            labelObject.transform.SetParent(
                dot.transform,
                false
            );

            label =
                labelObject.GetComponent<Text>();

            Text source =
                DotTextField != null
                    ? DotTextField.GetValue(
                        dot
                    ) as Text
                    : null;

            if (source != null)
            {
                label.font =
                    source.font;

                label.fontStyle =
                    source.fontStyle;

                label.color =
                    source.color;
            }

            label.fontSize =
                source != null
                    ? Mathf.Clamp(
                        Mathf.RoundToInt(
                            source.fontSize * 1.5f
                        ),
                        18,
                        27
                    )
                    : 21;

            label.alignment =
                TextAnchor.MiddleCenter;

            label.horizontalOverflow =
                HorizontalWrapMode.Overflow;

            label.verticalOverflow =
                VerticalWrapMode.Overflow;

            label.raycastTarget =
                false;

            Outline outline =
                labelObject.GetComponent<Outline>();

            outline.effectColor =
                new Color(
                    0f,
                    0f,
                    0f,
                    0.9f
                );

            outline.effectDistance =
                new Vector2(
                    1f,
                    -1f
                );
        }

        if (label == null)
        {
            return;
        }

        label.text =
            raftName ?? string.Empty;

        RectTransform rect =
            label.rectTransform;

        if (rect == null)
        {
            return;
        }

        rect.anchorMin =
            new Vector2(
                0.5f,
                0.5f
            );

        rect.anchorMax =
            new Vector2(
                0.5f,
                0.5f
            );

        rect.pivot =
            new Vector2(
                0.5f,
                0.5f
            );

        rect.anchoredPosition =
            new Vector2(
                0f,
                23f
            );

        rect.sizeDelta =
            new Vector2(
                Mathf.Clamp(
                    label.preferredWidth + 10f,
                    40f,
                    220f
                ),
                Mathf.Max(
                    20f,
                    label.preferredHeight + 4f
                )
            );

        labelObject.transform.SetAsLastSibling();
    }

    private static float GetFloatField(
        FieldInfo field,
        object target,
        float fallback)
    {
        if (field == null ||
            target == null)
        {
            return fallback;
        }

        object value =
            field.GetValue(target);

        return value is float
            ? (float)value
            : fallback;
    }

    private static void SetDotsActive(
        ReceiverState state,
        bool active)
    {
        foreach (Reciever_Dot dot
            in state.dots.Values)
        {
            if (dot != null)
            {
                dot.gameObject.SetActive(
                    active
                );
            }
        }
    }

    private static void RemoveDot(
        ReceiverState state,
        int raftId)
    {
        Reciever_Dot dot;

        if (!state.dots.TryGetValue(
                raftId,
                out dot))
        {
            return;
        }

        state.dots.Remove(
            raftId
        );

        if (dot != null)
        {
            UnityEngine.Object.Destroy(
                dot.gameObject
            );
        }
    }

    private static void DestroyState(
        ReceiverState state)
    {
        if (state == null)
        {
            return;
        }

        foreach (Reciever_Dot dot
            in state.dots.Values)
        {
            if (dot != null)
            {
                UnityEngine.Object.Destroy(
                    dot.gameObject
                );
            }
        }

        state.dots.Clear();
    }
}

public static class RaftRenameWindow
{
    private static GameObject panel;
    private static InputField input;
    private static Reciever targetReciever;
    private static int targetRaftId = int.MinValue;

    private static bool cursorVisible;
    private static CursorLockMode cursorLockMode;
    private static string previousActionMap;
    private static bool previousMovementFree;
    private static bool movementStateCaptured;
    private static int pendingCloseMode;

    public static bool IsOpen
    {
        get
        {
            return panel != null &&
                panel.activeSelf;
        }
    }

    public static void Open(
        Reciever reciever)
    {
        if (reciever == null)
        {
            return;
        }

        int raftId =
            RaftReceiverRadar.GetRaftId(
                reciever
            );

        if (raftId == int.MinValue)
        {
            return;
        }

        EnsureUI();

        if (panel == null ||
            input == null)
        {
            return;
        }

        targetReciever =
            reciever;

        targetRaftId =
            raftId;

        input.text =
            RaftNameRegistry.GetName(
                raftId
            );

        input.characterLimit =
            RaftNameRegistry.MaxNameLength;

        cursorVisible =
            Cursor.visible;

        cursorLockMode =
            Cursor.lockState;

        Cursor.visible = true;
        Cursor.lockState =
            CursorLockMode.None;

        PlayerInput playerInput =
            PlayerInput.GetPlayerByIndex(0);

        previousActionMap =
            playerInput != null &&
            playerInput.currentActionMap != null
                ? playerInput.currentActionMap.name
                : null;

        if (playerInput != null &&
            playerInput.actions != null &&
            playerInput.actions.FindActionMap(
                "UI",
                false
            ) != null)
        {
            playerInput.SwitchCurrentActionMap(
                "UI"
            );
        }

        Network_Player player =
            ComponentManager<Network_Player>.Value;

        if (player != null)
        {
            if (player.PersonController != null)
            {
                previousMovementFree =
                    player.PersonController
                        .IsMovementFree;

                movementStateCaptured =
                    true;

                player.PersonController
                    .IsMovementFree =
                        false;

                player.PersonController
                    .crouching =
                        false;

            }

            if (player.PlayerScript != null)
            {
                player.PlayerScript.SetMouseLookScripts(
                    false
                );
            }
        }

        pendingCloseMode = 0;

        panel.SetActive(true);
        panel.transform.SetAsLastSibling();

        input.Select();
        input.ActivateInputField();
        input.MoveTextEnd(false);
    }

    public static void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        Cursor.visible = true;
        Cursor.lockState =
            CursorLockMode.None;

        Network_Player localPlayer =
            ComponentManager<Network_Player>.Value;

        if (localPlayer != null &&
            localPlayer.PersonController != null)
        {
            localPlayer.PersonController
                .IsMovementFree =
                    false;

            localPlayer.PersonController
                .crouching =
                    false;

        }

        if (targetReciever == null)
        {
            Close(false);
            return;
        }

        Keyboard keyboard =
            Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        if (pendingCloseMode != 0)
        {
            bool closeKeyPressed =
                pendingCloseMode > 0
                    ? keyboard.enterKey.isPressed ||
                      keyboard.numpadEnterKey.isPressed
                    : keyboard.escapeKey.isPressed;

            if (!closeKeyPressed)
            {
                bool save =
                    pendingCloseMode > 0;

                pendingCloseMode = 0;

                Close(save);
            }

            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            pendingCloseMode = -1;
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            pendingCloseMode = 1;
        }
    }

    public static void Close(
        bool save)
    {
        if (!IsOpen)
        {
            targetReciever = null;
            targetRaftId = int.MinValue;
            pendingCloseMode = 0;
            return;
        }

        if (save &&
            targetRaftId != int.MinValue)
        {
            RebuildableRaft.RequestRaftNameChange(
                targetRaftId,
                input != null
                    ? input.text
                    : string.Empty
            );
        }

        if (input != null)
        {
            input.DeactivateInputField();
        }

        panel.SetActive(false);

        PlayerInput playerInput =
            PlayerInput.GetPlayerByIndex(0);

        if (playerInput != null &&
            playerInput.actions != null &&
            !string.IsNullOrEmpty(previousActionMap) &&
            playerInput.actions.FindActionMap(
                previousActionMap,
                false
            ) != null)
        {
            playerInput.SwitchCurrentActionMap(
                previousActionMap
            );
        }

        Network_Player player =
            ComponentManager<Network_Player>.Value;

        if (player != null)
        {
            if (movementStateCaptured &&
                player.PersonController != null)
            {
                player.PersonController
                    .IsMovementFree =
                        previousMovementFree;
            }

            if (player.PlayerScript != null)
            {
                player.PlayerScript.SetMouseLookScripts(
                    true
                );
            }
        }

        movementStateCaptured = false;
        pendingCloseMode = 0;

        Cursor.visible =
            cursorVisible;

        Cursor.lockState =
            cursorLockMode;

        targetReciever = null;
        targetRaftId = int.MinValue;
        previousActionMap = null;
    }

    public static void CloseIfReceiver(
        Reciever reciever)
    {
        if (reciever != null &&
            targetReciever == reciever)
        {
            Close(false);
        }
    }

    private static void EnsureUI()
    {
        if (panel != null &&
            input != null)
        {
            return;
        }

        CanvasHelper canvas =
            ComponentManager<CanvasHelper>.Value;

        if (canvas == null)
        {
            return;
        }

        Font font =
            null;

        Text existingText =
            canvas.GetComponentInChildren<Text>(
                true
            );

        if (existingText != null)
        {
            font =
                existingText.font;
        }

        if (font == null)
        {
            font =
                Resources.GetBuiltinResource<Font>(
                    "Arial.ttf"
                );
        }

        panel =
            new GameObject(
                "RebuildableRaft_RenameWindow",
                typeof(RectTransform),
                typeof(Image)
            );

        panel.transform.SetParent(
            canvas.transform,
            false
        );

        RectTransform panelRect =
            panel.GetComponent<RectTransform>();

        panelRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        panelRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        panelRect.pivot =
            new Vector2(0.5f, 0.5f);

        panelRect.sizeDelta =
            new Vector2(480f, 180f);

        panelRect.anchoredPosition =
            Vector2.zero;

        Image panelImage =
            panel.GetComponent<Image>();

        panelImage.color =
            new Color(
                0.05f,
                0.08f,
                0.10f,
                0.96f
            );

        CreateText(
            panel.transform,
            font,
            "Raft name",
            24,
            new Vector2(0f, 52f),
            new Vector2(430f, 36f),
            TextAnchor.MiddleCenter
        );

        GameObject inputObject =
            new GameObject(
                "Input",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField)
            );

        inputObject.transform.SetParent(
            panel.transform,
            false
        );

        RectTransform inputRect =
            inputObject.GetComponent<RectTransform>();

        inputRect.anchorMin =
            new Vector2(0.5f, 0.5f);

        inputRect.anchorMax =
            new Vector2(0.5f, 0.5f);

        inputRect.pivot =
            new Vector2(0.5f, 0.5f);

        inputRect.sizeDelta =
            new Vector2(400f, 46f);

        inputRect.anchoredPosition =
            new Vector2(0f, 2f);

        Image inputImage =
            inputObject.GetComponent<Image>();

        inputImage.color =
            new Color(
                0.15f,
                0.19f,
                0.22f,
                1f
            );

        Text text =
            CreateText(
                inputObject.transform,
                font,
                string.Empty,
                20,
                Vector2.zero,
                new Vector2(370f, 42f),
                TextAnchor.MiddleLeft
            );

        Font inputFont =
            Resources.GetBuiltinResource<Font>(
                "Arial.ttf"
            );

        if (inputFont != null)
        {
            text.font =
                inputFont;

            text.fontStyle =
                FontStyle.Normal;
        }

        Text placeholder =
            CreateText(
                inputObject.transform,
                inputFont != null
                    ? inputFont
                    : font,
                "Enter raft name",
                20,
                Vector2.zero,
                new Vector2(370f, 42f),
                TextAnchor.MiddleLeft
            );

        placeholder.fontStyle =
            FontStyle.Normal;

        placeholder.color =
            new Color(
                1f,
                1f,
                1f,
                0.35f
            );

        input =
            inputObject.GetComponent<InputField>();

        input.textComponent =
            text;

        input.placeholder =
            placeholder;

        input.lineType =
            InputField.LineType.SingleLine;

        input.contentType =
            InputField.ContentType.Standard;

        input.characterLimit =
            RaftNameRegistry.MaxNameLength;

        CreateText(
            panel.transform,
            font,
            "Enter - Save     Esc - Cancel",
            16,
            new Vector2(0f, -52f),
            new Vector2(430f, 30f),
            TextAnchor.MiddleCenter
        );

        panel.SetActive(false);
    }

    private static Text CreateText(
        Transform parent,
        Font font,
        string value,
        int fontSize,
        Vector2 position,
        Vector2 size,
        TextAnchor alignment)
    {
        GameObject textObject =
            new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(Text)
            );

        textObject.transform.SetParent(
            parent,
            false
        );

        RectTransform rect =
            textObject.GetComponent<RectTransform>();

        rect.anchorMin =
            new Vector2(0.5f, 0.5f);

        rect.anchorMax =
            new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            size;

        rect.anchoredPosition =
            position;

        Text text =
            textObject.GetComponent<Text>();

        text.font =
            font;

        text.fontSize =
            fontSize;

        text.alignment =
            alignment;

        text.color =
            Color.white;

        text.text =
            value;

        return text;
    }
}

[HarmonyPatch]
public static class RaftRename_MenuInputBlock
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        MethodInfo pauseUpdate =
            AccessTools.Method(
                typeof(PauseMenu),
                "Update"
            );

        if (pauseUpdate != null)
        {
            yield return pauseUpdate;
        }

        MethodInfo canvasUpdate =
            AccessTools.Method(
                typeof(CanvasHelper),
                "Update"
            );

        if (canvasUpdate != null)
        {
            yield return canvasUpdate;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !RaftRenameWindow.IsOpen;
    }
}

[HarmonyPatch]
public static class PersonController_GroundControll_RaftRename
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(PersonController),
            "GroundControll"
        );
    }

    [HarmonyPostfix]
    private static void Postfix(
        PersonController __instance)
    {
        if (!RaftRenameWindow.IsOpen ||
            __instance == null)
        {
            return;
        }

        __instance.crouching =
            false;
    }
}

[HarmonyPatch]
public static class PlayerInventory_Update_RaftRename
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        MethodInfo update =
            AccessTools.Method(
                typeof(PlayerInventory),
                "Update"
            );

        if (update != null)
        {
            yield return update;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !RaftRenameWindow.IsOpen;
    }
}

[HarmonyPatch]
public static class NoteBookUI_Update_RaftRename
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        MethodInfo update =
            AccessTools.Method(
                typeof(NoteBookUI),
                "Update"
            );

        if (update != null)
        {
            yield return update;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !RaftRenameWindow.IsOpen;
    }
}

[HarmonyPatch]
public static class ChatTextFieldController_Update_RaftRename
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        MethodInfo update =
            AccessTools.Method(
                typeof(ChatTextFieldController),
                "Update"
            );

        if (update != null)
        {
            yield return update;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !RaftRenameWindow.IsOpen;
    }
}

[HarmonyPatch(typeof(Reciever), "Update")]
public static class Reciever_Update_RaftRadar
{
    [HarmonyPostfix]
    public static void Postfix(
        Reciever __instance)
    {
        RaftReceiverRadar.UpdateReceiver(
            __instance
        );
    }
}

[HarmonyPatch(typeof(Reciever), "OnDestroy")]
public static class Reciever_OnDestroy_RaftRadar
{
    [HarmonyPrefix]
    public static void Prefix(
        Reciever __instance)
    {
        RaftRenameWindow.CloseIfReceiver(
            __instance
        );

        RaftReceiverRadar.RemoveReceiver(
            __instance
        );
    }
}

[HarmonyPatch]
public static class Reciever_OnIsRayed_RaftRename
{
    private static MethodBase TargetMethod()
    {
        InterfaceMapping map =
            typeof(Reciever).GetInterfaceMap(
                typeof(IRaycastable)
            );

        for (int i = 0;
             i < map.InterfaceMethods.Length;
             i++)
        {
            if (map.InterfaceMethods[i].Name ==
                "OnIsRayed")
            {
                return map.TargetMethods[i];
            }
        }

        return null;
    }

    [HarmonyPrefix]
    public static bool Prefix()
    {
        return !RaftRenameWindow.IsOpen;
    }

    [HarmonyPostfix]
    public static void Postfix(
        Reciever __instance)
    {
        if (__instance == null ||
            RaftRenameWindow.IsOpen ||
            CanvasHelper.ActiveMenu != MenuType.None ||
            ChatTextFieldController.IsChatWindowSelected ||
            !Helper.LocalPlayerIsWithinDistance(
                __instance.transform.position,
                Player.UseDistance
            ))
        {
            return;
        }

        int raftId =
            RaftReceiverRadar.GetRaftId(
                __instance
            );

        if (raftId == int.MinValue)
        {
            return;
        }

        CanvasHelper canvas =
            ComponentManager<CanvasHelper>.Value;

        if (canvas != null &&
            canvas.displayTextManager != null)
        {
            canvas.displayTextManager.ShowText(
                "Set raft name",
                "N",
                KeyCode.N,
                1,
                0,
                false
            );
        }

        Keyboard keyboard =
            Keyboard.current;

        if (keyboard != null &&
            keyboard.nKey.wasPressedThisFrame)
        {
            RaftRenameWindow.Open(
                __instance
            );
        }
    }
}
