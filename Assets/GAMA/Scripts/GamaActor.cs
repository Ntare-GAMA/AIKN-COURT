using System.Collections.Generic;
using UnityEngine;

// Thin wrapper around a humanoid Animator whose controller has one looping state per Mixamo clip.
public class GamaActor : MonoBehaviour
{
    public string displayName;
    public int team; // 0 none, 1 red, 2 black
    public Animator anim;

    string current;
    float stateStart;
    Dictionary<string, float> lengths;

    void Awake() { Init(); }

    public void Init()
    {
        if (anim == null) anim = GetComponent<Animator>();
        if (lengths != null || anim == null || anim.runtimeAnimatorController == null) return;
        lengths = new Dictionary<string, float>();
        foreach (var c in anim.runtimeAnimatorController.animationClips)
            lengths[c.name] = Mathf.Max(0.1f, c.length);
    }

    public bool HasState(string s)
    {
        Init();
        return lengths != null && lengths.ContainsKey(s);
    }

    public string Current => current;

    // Runtime: crossfade only when the state changes.
    public void SetState(string s, float fade = 0.3f)
    {
        Init();
        if (s == current || !HasState(s)) return;
        current = s;
        stateStart = Time.time;
        anim.CrossFadeInFixedTime(s, fade, 0, Random.Range(0f, 0.5f));
    }

    // Editor/seek: pose the character exactly at a time inside a state.
    public void SeekState(string s, float timeInState)
    {
        Init();
        if (!HasState(s)) return;
        current = s;
        if (!anim.isInitialized) anim.Rebind();
        anim.Play(s, 0, Mathf.Repeat(timeInState / lengths[s], 1f));
        anim.Update(0.0001f); // applies the Play request (edit mode needs a non-zero step)
        anim.Update(0f);       // evaluates the new pose
    }

    public Transform Bone(HumanBodyBones b) => anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;

    public Vector3 HandPos
    {
        get
        {
            var h = Bone(HumanBodyBones.RightHand);
            return h != null ? h.position : transform.position + Vector3.up * 1.0f + transform.right * 0.3f;
        }
    }

    public Vector3 ReleasePoint => transform.position + Vector3.up * 2.35f + transform.forward * 0.25f;
}
