using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>GunnerRig inspector (grouped fields, setup buttons) and scene handles: emitter pose, range, aim fan.</summary>
    [CustomEditor(typeof(GunnerRig)), CanEditMultipleObjects]
    public class GunnerRigEditor : GunnerInspectorBase
    {
        const string HandlesPref = "Mecaota.Gunner.ShowHandles";
        static readonly Color FanFill = new Color(1f, 0.6f, 0.1f, 0.12f);
        static readonly Color FanLine = new Color(1f, 0.6f, 0.1f, 0.8f);
        static readonly Color RangeLine = new Color(0.3f, 0.8f, 1f, 0.9f);

        protected override void DrawBody()
        {
            var rig = (GunnerRig)target;

            if (Foldout("GunnerRig.section.emitters"))
            {
                Field("emitters");
                Field("shotParticles");
                Field("mounts");
                Field("linkBones");
                Field("movers");
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(targets.Length != 1))
                    {
                        if (GUILayout.Button(T("button.add_emitter")))
                        {
                            GunnerSetup.AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button(T("button.place_hand")))
                        {
                            Selection.activeObject = GunnerSetup.PlaceHand(rig).gameObject;
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                bool show = EditorPrefs.GetBool(HandlesPref, true);
                bool next = EditorGUILayout.Toggle(Label("GunnerRig.showHandles"), show);
                if (next != show)
                {
                    EditorPrefs.SetBool(HandlesPref, next);
                    SceneView.RepaintAll();
                }
            }

            if (Foldout("GunnerRig.section.receivers"))
            {
                Field("projectile");
                Field("receivers");
                Field("stream");
                using (new EditorGUI.DisabledScope(targets.Length != 1))
                {
                    if (GUILayout.Button(T("button.add_splash")))
                    {
                        GunnerSetup.AddSplash(rig);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(T("button.add_sounds")))
                    {
                        Selection.activeObject = GunnerSetup.AddSounds(rig).gameObject;
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (Foldout("GunnerRig.section.shot"))
            {
                Field("fireMode");
                Field("shotSpeed");
                Field("maxRange");
                Field("castRadius");
                Field("hitMask");
                Field("hitPlayers");
                Field("excludeOwner");
                Field("playerRadius");
                Field("aimMaxAngle");
                Field("maxHits");
            }

            if (Foldout("GunnerRig.section.stream"))
            {
                Field("streamRate");
                Field("streamWidth");
                Field("streamTaper");
                Field("streamJoin");
                Field("streamHitMode");
                Field("streamHitInterval");
                Field("streamCollide");
            }

            if (Foldout("GunnerRig.section.bone"))
            {
                Field("boneFollow");
                Field("boneStrength");
                Field("boneYawMax");
                Field("bonePitchUpMax");
                Field("bonePitchDownMax");
                Field("boneSmooth");
                Field("boneMinSpeed");
            }
        }

        // ---------------- Scene view ----------------

        void OnSceneGUI()
        {
            var rig = (GunnerRig)target;
            if (rig == null || rig.emitters == null) return;
            bool edit = EditorPrefs.GetBool(HandlesPref, true) && !Application.isPlaying;
            for (int i = 0; i < rig.emitters.Length; i++)
            {
                var em = rig.emitters[i];
                if (em == null) continue;
                DrawEmitter(rig, em, i, edit);
            }
        }

        void DrawEmitter(GunnerRig rig, Transform em, int index, bool edit)
        {
            Vector3 pos = em.position;
            Vector3 fwd = em.forward;
            Vector3 up = em.up;
            Vector3 right = em.right;
            float size = HandleUtility.GetHandleSize(pos);
            float range = Mathf.Max(0f, rig.maxRange);
            float angle = Mathf.Clamp(rig.aimMaxAngle, 0f, 180f);
            float fan = Mathf.Min(range, size * 1.5f);

            // The aim reaches this far from the rest direction: one fan for yaw, one for pitch.
            Handles.color = FanFill;
            Handles.DrawSolidArc(pos, up, Quaternion.AngleAxis(-angle, up) * fwd, angle * 2f, fan);
            Handles.DrawSolidArc(pos, right, Quaternion.AngleAxis(-angle, right) * fwd, angle * 2f, fan);
            Handles.color = FanLine;
            Handles.DrawWireArc(pos, up, Quaternion.AngleAxis(-angle, up) * fwd, angle * 2f, fan);
            Handles.DrawWireArc(pos, right, Quaternion.AngleAxis(-angle, right) * fwd, angle * 2f, fan);

            Handles.color = RangeLine;
            Handles.DrawDottedLine(pos, pos + fwd * range, 4f);
            Handles.Label(pos + up * size * 0.3f, index + " " + em.name);

            if (!edit) return;

            // Pose: follows the scene tool (move / rotate), so it never fights the default transform handle.
            // An emitter on the rig's own Transform already has Unity's handle; a second one would fight it.
            bool own = em == rig.transform;
            if (!own && Tools.current == Tool.Move)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 p = Handles.PositionHandle(pos, Tools.pivotRotation == PivotRotation.Local ? em.rotation : Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(em, "Gunner: move emitter");
                    em.position = p;
                }
            }
            else if (!own && Tools.current == Tool.Rotate)
            {
                EditorGUI.BeginChangeCheck();
                Quaternion r = Handles.RotationHandle(em.rotation, pos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(em, "Gunner: turn emitter");
                    em.rotation = r;
                }
            }

            // Range and aim limit belong to the rig: edited on the first emitter only.
            if (index != 0) return;
            Handles.color = RangeLine;
            EditorGUI.BeginChangeCheck();
            Vector3 tip = Handles.Slider(pos + fwd * range, fwd, size * 0.12f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float nextRange = Mathf.Max(0.1f, Vector3.Dot(tip - pos, fwd));
                GunnerEditorUtil.Edit(rig, so => so.FindProperty("maxRange").floatValue = nextRange);
            }

            Handles.color = FanLine;
            Vector3 edge = pos + Quaternion.AngleAxis(angle, up) * fwd * fan;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(edge, size * 0.08f, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 flat = Vector3.ProjectOnPlane(moved - pos, up);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    float nextAngle = Mathf.Clamp(Vector3.Angle(fwd, flat), 1f, 180f);
                    GunnerEditorUtil.Edit(rig, so => so.FindProperty("aimMaxAngle").floatValue = Mathf.Round(nextAngle));
                }
            }
        }
    }
}
