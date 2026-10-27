namespace OpenScct.Server;

/// <summary>
/// One tick of player input for a <see cref="CubeWalker"/>, as the client sends it. TimeStamp is the client
/// tick the move was made on (always above 0), ViewYaw the absolute yaw in rotator units.
/// </summary>
public readonly record struct CubeMove(uint TimeStamp, sbyte Forward, sbyte Strafe, ushort ViewYaw, byte Buttons);

/// <summary>Input the host sets on a <see cref="ClientSession"/> before each tick.</summary>
public readonly record struct CubeInput(sbyte Forward, sbyte Strafe, ushort ViewYaw, byte Buttons = 0);
