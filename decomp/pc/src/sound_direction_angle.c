typedef struct { int x, y, z; } FixedVector;
typedef struct { FixedVector position, velocity, forward, right; } ReceiverPose;
extern ReceiverPose *FirstSoundReceiver;
extern float __cdecl approximate_atan(float tangent);

static __forceinline float angle_from_projection(float x, float y)
{
    float angle;
    if (x == 0) {
        if (y == 0) return 0;
        if (y > 0) return 0.25f;
        return 0.75f;
    }
    if (y == 0) {
        if (x > 0) return 0;
        return 0.5f;
    }
    angle = approximate_atan(y / x);
    if (angle < 0) {
        if (y > 0) angle += 0.5f;
        else angle += 1.0f;
    } else if (angle > 0) {
        if (y < 0) angle += 0.5f;
    } else if (y > 0)
        angle += 0.5f;
    return angle;
}

#pragma code_seg(".text$V75c420")
float __fastcall direction_in_turns(const FixedVector *position)
{
    ReceiverPose *receiver = FirstSoundReceiver;
    FixedVector delta;
    float dx, dy, dz, x, y;
    if (!receiver)
        return 0.0f;
    delta.x = position->x - receiver->position.x;
    delta.y = position->y - receiver->position.y;
    delta.z = position->z - receiver->position.z;
    dz = delta.z / 65536.0f;
    dy = delta.y / 65536.0f;
    dx = delta.x / 65536.0f;
    x = dx * (receiver->forward.x / 65536.0f)
        + (dy * (receiver->forward.y / 65536.0f) + dz * (receiver->forward.z / 65536.0f));
    y = dx * (receiver->right.x / 65536.0f)
        + (dy * (receiver->right.y / 65536.0f) + dz * (receiver->right.z / 65536.0f));
    return angle_from_projection(x, y);
}
