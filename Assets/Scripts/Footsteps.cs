using UnityEngine;

// A person's footsteps (Npc adds it): "npc step" each time a foot comes down in the walk or run animation, where the
// foot is. A foot's down when its height above the person's root stops falling near the lowest it's been lately, so
// the sound lands with the animation at any pace. Only people moving within earshot of the camera step out loud.
public class Footsteps : MonoBehaviour
{
    public string sound = "npc step";
    public float earshot = 25, minSpeed = 0.3f;

    Animator animator;
    Health health;
    Transform[] feet;
    readonly float[] height = new float[2], low = { 0, 0 }, lastStep = { -1, -1 };
    readonly bool[] falling = new bool[2];
    Vector3 lastPosition;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        if (!animator || !animator.isHuman) { enabled = false; return; }
        feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
        if (!feet[0] || !feet[1]) { enabled = false; return; }
        lastPosition = transform.position;
        for (int i = 0; i < 2; i++) low[i] = height[i] = feet[i].position.y - transform.position.y;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        float speed = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude / dt;
        lastPosition = transform.position;
        Camera cam = Camera.main;
        bool heard = cam && (cam.transform.position - transform.position).sqrMagnitude < earshot * earshot
            && speed > minSpeed && !(health && health.IsDead);

        for (int i = 0; i < 2; i++)
        {
            float h = feet[i].position.y - transform.position.y;
            low[i] = Mathf.Min(h, low[i] + 0.05f * dt); // the lowest lately, creeping back up so a new floor is learned
            bool nowFalling = h < height[i] - 0.0005f;
            // a foot that was coming down has stopped, at the bottom of its stride
            if (falling[i] && !nowFalling && h < low[i] + 0.04f && Time.time - lastStep[i] > 0.2f)
            {
                lastStep[i] = Time.time;
                if (heard) Sfx.Play(sound, feet[i].position);
            }
            falling[i] = nowFalling;
            height[i] = h;
        }
    }
}
