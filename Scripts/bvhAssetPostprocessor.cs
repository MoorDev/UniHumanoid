﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;


namespace UniHumanoid
{
    public class bvhAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                var ext = Path.GetExtension(path).ToLower();
                if (ext == ".bvh")
                {
                    ImportBvh(path);
                }
            }
        }

        static void ImportBvh(string srcPath)
        {
            Debug.LogFormat("ImportBvh: {0}", srcPath);

            var src = File.ReadAllText(srcPath, System.Text.Encoding.UTF8);
            var bvh = Bvh.Parse(src);
            Debug.LogFormat("parsed {0}", bvh);

            using (var context = new PrefabContext(srcPath))
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(srcPath));

                context.SetMainGameObject(root.name, root);

                BuildHierarchy(root.transform, bvh.Root, 1.0f);

                var minY = 0.0f;
                foreach (var x in root.transform.Traverse())
                {
                    if (x.position.y < minY)
                    {
                        minY = x.position.y;
                    }
                }

                var toMeter = 1.0f/(-minY);
                Debug.LogFormat("minY: {0} {1}", minY, toMeter);
                foreach (var x in root.transform.Traverse())
                {
                    x.localPosition *= toMeter;
                }

                // foot height to 0
                root.transform.GetChild(0).position = new Vector3(0, -minY * toMeter, 0);

                var clip = CreateAnimationClip(bvh, toMeter);
                clip.name = root.name;
                clip.legacy = true;
                clip.wrapMode = WrapMode.Loop;
                context.AddObjectToAsset(clip.name, clip);

                var animation = root.AddComponent<Animation>();
                animation.AddClip(clip, clip.name);
                animation.clip = clip;
                animation.Play();
            }
        }

        class CurveSet
        {

            BvhNode Node;
            Func<float, float, float, Quaternion> EulerToRotation;
            public CurveSet(BvhNode node)
            {
                Node = node;
            }

            public ChannelCurve PositionX;
            public ChannelCurve PositionY;
            public ChannelCurve PositionZ;
            public Vector3 GetPosition(int i)
            {
                return new Vector3(
                    PositionX.Keys[i],
                    PositionY.Keys[i],
                    PositionZ.Keys[i]);
            }

            public ChannelCurve RotationX;
            public ChannelCurve RotationY;
            public ChannelCurve RotationZ;
            public Quaternion GetRotation(int i)
            {
                if (EulerToRotation == null)
                {
                    EulerToRotation = Node.GetEulerToRotation();
                }
                return EulerToRotation(
                    RotationX.Keys[i],
                    RotationY.Keys[i],
                    RotationZ.Keys[i]
                    );
            }

            static void AddCurve(Bvh bvh, AnimationClip clip, ChannelCurve ch, float toMeter)
            {
                if (ch == null) return;
                var pathWithProp = default(Bvh.PathWithProperty);
                bvh.TryGetPathWithPropertyFromChannel(ch, out pathWithProp);
                var curve = new AnimationCurve();
                for (int i = 0; i < bvh.FrameCount; ++i)
                {
                    var time = (float)(i * bvh.FrameTime.TotalSeconds);
                    var value = ch.Keys[i] * toMeter;
                    curve.AddKey(time, value);
                }
                clip.SetCurve(pathWithProp.Path, typeof(Transform), pathWithProp.Property, curve);
            }

            public void AddCurves(Bvh bvh, AnimationClip clip, float toMeter)
            {
                AddCurve(bvh, clip, PositionX, toMeter);
                AddCurve(bvh, clip, PositionY, toMeter);
                AddCurve(bvh, clip, PositionZ, toMeter);

                var pathWithProp = default(Bvh.PathWithProperty);
                bvh.TryGetPathWithPropertyFromChannel(RotationX, out pathWithProp);

                // rotation
                var curveX = new AnimationCurve();
                var curveY = new AnimationCurve();
                var curveZ = new AnimationCurve();
                var curveW = new AnimationCurve();
                for (int i = 0; i < bvh.FrameCount; ++i)
                {
                    var time = (float)(i * bvh.FrameTime.TotalSeconds);
                    var q = GetRotation(i);
                    curveX.AddKey(time, q.x);
                    curveY.AddKey(time, q.y);
                    curveZ.AddKey(time, q.z);
                    curveW.AddKey(time, q.w);
                }
                clip.SetCurve(pathWithProp.Path, typeof(Transform), "localRotation.x", curveX);
                clip.SetCurve(pathWithProp.Path, typeof(Transform), "localRotation.y", curveY);
                clip.SetCurve(pathWithProp.Path, typeof(Transform), "localRotation.z", curveZ);
                clip.SetCurve(pathWithProp.Path, typeof(Transform), "localRotation.w", curveW);
            }
        }

        static AnimationClip CreateAnimationClip(Bvh bvh, float toMeter)
        {
            var clip = new AnimationClip();

            Dictionary<BvhNode, CurveSet> curveMap = new Dictionary<BvhNode, CurveSet>();

            int j = 0;
            foreach(var node in bvh.Root.Traverse())
            {
                var set = new CurveSet(node);
                curveMap[node] = set;

                for(int i=0; i<node.Channels.Length; ++i, ++j)
                {
                    var curve = bvh.Channels[j];
                    switch(node.Channels[i])
                    {
                        case Channel.Xposition: set.PositionX = curve; break;
                        case Channel.Yposition: set.PositionY = curve; break;
                        case Channel.Zposition: set.PositionZ = curve; break;
                        case Channel.Xrotation: set.RotationX = curve; break;
                        case Channel.Yrotation: set.RotationY = curve; break;
                        case Channel.Zrotation: set.RotationZ = curve; break;
                        default: throw new Exception();
                    }
                }
            }

            foreach(var set in curveMap)
            {
                set.Value.AddCurves(bvh, clip, toMeter);
            }

            return clip;
        }

        static void BuildHierarchy(Transform parent, BvhNode node, float toMeter)
        {
            var go = new GameObject(node.Name);
            go.transform.localPosition = node.Offset * toMeter;
            go.transform.SetParent(parent, false);

            var gizmo=go.AddComponent<BoneGizmoDrawer>();

            foreach(var child in node.Children)
            {
                BuildHierarchy(go.transform, child, toMeter);
            }
        }
    }
}
