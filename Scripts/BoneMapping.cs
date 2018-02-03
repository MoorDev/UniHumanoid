﻿using UnityEngine;
using System.Linq;
using System;

namespace UniHumanoid
{
    public class BoneMapping : MonoBehaviour
    {
        [SerializeField]
        public GameObject[] Bones;

        private void Reset()
        {
            Bones = new GameObject[(int)HumanBodyBones.LastBone];

            var animator = GetComponent<Animator>();
            if (animator != null)
            {
                if (animator.avatar != null)
                {
                    foreach(HumanBodyBones key in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (key == HumanBodyBones.LastBone)
                        {
                            break;
                        }
                        var transform = animator.GetBoneTransform(key);
                        if (transform != null)
                        {
                            Bones[(int)key] = transform.gameObject;
                        }
                    }
                }
            }
        }

        public void GuessBoneMapping()
        {
            var hips = Bones[(int)HumanBodyBones.Hips];
            if (hips == null)
            {
                Debug.LogWarning("require hips");
                return;
            }

            var bones = HumanoidUtility.TraverseSkeleton(hips.transform, 
                hips.transform.Traverse().ToArray()).ToArray();
            foreach (var x in bones)
            {
                Bones[(int)x.Key] = x.Value.gameObject;
            }
        }

        public void EnsureTPose()
        {
            var map = Bones
                .Select((x, i) => new { i, x })
                .Where(x => x.x != null)
                .ToDictionary(x => (HumanBodyBones)x.i, x => x.x.transform)
                ;
            {
                var left = (map[HumanBodyBones.LeftLowerArm].position - map[HumanBodyBones.LeftUpperArm].position).normalized;
                map[HumanBodyBones.LeftUpperArm].rotation = Quaternion.FromToRotation(left, Vector3.left) * map[HumanBodyBones.LeftUpperArm].rotation;
            }
            {
                var right = (map[HumanBodyBones.RightLowerArm].position - map[HumanBodyBones.RightUpperArm].position).normalized;
                map[HumanBodyBones.RightUpperArm].rotation = Quaternion.FromToRotation(right, Vector3.right) * map[HumanBodyBones.RightUpperArm].rotation;
            }
        }

        public Avatar CreateAvatar()
        {
            var map = Bones
                .Select((x, i) => new { i, x })
                .Where(x => x.x != null)
                .ToDictionary(x => (HumanBodyBones)x.i, x => x.x.transform)
                ;

            var copy = Instantiate(gameObject);
            try
            {
                var description = new HumanDescription
                {
                    human = map.Select(x =>
                    {
                        var hb = new HumanBone
                        {
                            boneName = x.Value.name,
                            humanName = HumanoidUtility.ToHumanBoneName(x.Key)
                        };
                        hb.limit.useDefaultValues = true;
                        return hb;
                    }).ToArray(),
                    skeleton = copy.transform.Traverse().Select(x => x.ToSkeletonBone()).ToArray(),
                    lowerArmTwist = 0.5f,
                    upperArmTwist = 0.5f,
                    upperLegTwist = 0.5f,
                    lowerLegTwist = 0.5f,
                    armStretch = 0.05f,
                    legStretch = 0.05f,
                    feetSpacing = 0.0f,
                };

                var avatar=AvatarBuilder.BuildHumanAvatar(copy, description);
                avatar.name = name;

                var animator = GetComponent<Animator>();
                if (animator != null)
                {
                    animator.avatar = avatar;
                }

                return avatar;
            }
            finally
            {
                if (Application.isEditor)
                {
                    DestroyImmediate(copy);
                }
                else
                {
                    Destroy(copy);
                }
            }
        }
    }
}
