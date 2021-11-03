﻿/**
 * UNITY PARTICLE SYSTEM -> FLYFF .SFX EXPORT SCRIPT BY FROSTIAE(#2809 DISCORD)
 */

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

public class SFXExport : MonoBehaviour
{
    static GameObject prefab;
    static readonly string sourceTexDir = "Assets/Resources/SFXTextures";
    static readonly string exportDir = "Assets/Exports";
    static readonly float curveSubdivisions = 20f;   // How many keyframes the curve will be split into

    [MenuItem("Tools/Export Selected Particle Prefab to .SFX")]
    static void ExportSFX()
    {
        prefab = Selection.activeGameObject;
        if (!prefab)
        {
            Debug.LogError("No prefab selected.");
            return;
        }

        string fileName = prefab.name;
        BinaryWriter file = new BinaryWriter(File.Open("Assets/Exports/" + fileName + ".sfx", FileMode.Create));

        file.Flush();
        SaveData(file);
        file.Close();

        Debug.Log("Particle done exporting to SFX.");
    }

    static void SaveData(BinaryWriter file)
    {
        string version = "SFX0.3  ";
        file.Write(version.ToCharArray());

        int numParts = prefab.transform.childCount;
        file.Write(numParts + 1);                       // # of parts

        // TODO: Currently only supporting bill (1)
        file.Write(1);                                  // Type (Bill/Particle)
        if (!SaveBill(prefab, file))
            return;

        // Save all the children as well
        for (int i = 0; i < numParts; i++)
        {
            file.Write(1);
            if (!SaveBill(prefab.transform.GetChild(i).gameObject, file))
                return;
        }
    }

    /// <summary>
    /// Save a CSFXPartBill object.
    /// </summary>
    static bool SaveBill(GameObject part, BinaryWriter file)
    {
        List<sfxKeyframe> keyframes = new List<sfxKeyframe>();

        file.Write(part.name.Length);                   // Name length
        file.Write(part.name.ToCharArray());            // Name

        ParticleSystem ps = part.GetComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = part.GetComponent<ParticleSystemRenderer>();
        var emission = ps.emission;

        if (ps.main.startSpeed.constant > 0)
        {
            Debug.LogError("Start speed in the main module is not supported. Set it to 0 and Use Velocity over lifetime instead.");
            return false;
        }

        if (emission.rateOverTime.constant > 0 || 
            emission.rateOverDistance.constant > 0 ||
            emission.burstCount < 1)
        {
            Debug.LogError("Only bursts are supported in the emission module. Set the burst count to 1 and do not use rate over time or distance.");
            return false;
        }

        if (!CheckModules(ps))
            Debug.LogWarning("There are some active modules that are not supported, and will not be included in the .SFX file. Supported modules" +
                "are: Emission, Velocity over lifetime, Rotation over lifetime, Color over lifetime, Size over lifetime, Noise.");

        Material mat = renderer.sharedMaterial;
        Texture texture = mat.mainTexture;

        // Getting the texture file name
        string texOut = texture.name;
        string[] fileEntries = Directory.GetFiles(sourceTexDir);
        foreach (string fileName in fileEntries)
        {
            string s = Path.GetFileNameWithoutExtension(fileName);
            if (s == texture.name)
            {
                texOut = Path.GetFileName(fileName);
                break;
            }
        }

        file.Write(texOut.Length);                      // Texture name legnth
        file.Write(texOut.ToCharArray());               // Texture name


        // Copying texture file to exports directory
        // Only supporting .dds for now
        File.Copy(Path.Combine(sourceTexDir, texOut), Path.Combine(exportDir + "/Texture", texOut), true);

        // Can't do multiple texture frames since unity does spritesheets and flyff doesn't
        ushort texFrame = 1;
        ushort texLoop = 1;
        file.Write(texFrame);                           // Texture frames
        file.Write(texLoop);                            // Texture loop

        int visible = renderer.enabled ? 1 : 0;
        file.Write(visible);                            // Visible

        switch (renderer.renderMode)
        {
            case ParticleSystemRenderMode.Billboard:
                if (renderer.alignment == ParticleSystemRenderSpace.World)
                    file.Write(4);                      // Normal bill type
                else
                    file.Write(1);                      // Billboard bill type
                break;
            case ParticleSystemRenderMode.HorizontalBillboard:
                file.Write(2);                          // Bottom bill type
                break;
            default:
                file.Write(4);                          // Normal bill type
                break;
        }

        switch (mat.GetFloat("_Mode"))
        {
            case 4:
                file.Write(2);                          // Glow alpha type
                break;
            case 2:
                file.Write(1);                          // Blend alpha type
                break;
            default:
                file.Write(2);                          // Glow alpha type
                break;
        }

        // INDIVIDUAL MODULES
        SaveSizes(ps, file, keyframes);
        SaveRotations(ps, file, keyframes);
        SaveNoise(ps, file, keyframes);
        SaveVelocity(ps, file, keyframes);
        SaveTransformPosition(ps, file, keyframes);
        SaveOrbitalVelocity(ps, file, keyframes);

        // Colors need to be done at the end because the alpha needs to be applied to whatever keyframes exist by now
        SaveColors(ps, file, keyframes);
        
        int numKeys = emission.enabled ? keyframes.Count() : 0;
        file.Write(numKeys);                            // # of keyframes

        WriteKeyframes(file, keyframes);
        return true;
    }


    /// <returns>True if the enabled modules are supported, false otherwise.</returns>
    static bool CheckModules(ParticleSystem ps)
    {
        if (ps.shape.enabled)                       return false;
        if (ps.limitVelocityOverLifetime.enabled)   return false;
        if (ps.inheritVelocity.enabled)             return false;
        if (ps.forceOverLifetime.enabled)           return false;
        if (ps.colorBySpeed.enabled)                return false;
        if (ps.sizeBySpeed.enabled)                 return false;
        if (ps.rotationBySpeed.enabled)             return false;
        if (ps.externalForces.enabled)              return false;
        if (ps.collision.enabled)                   return false;
        if (ps.trigger.enabled)                     return false;
        if (ps.subEmitters.enabled)                 return false;
        if (ps.textureSheetAnimation.enabled)       return false;
        if (ps.lights.enabled)                      return false;
        if (ps.trails.enabled)                      return false;
        if (ps.customData.enabled)                  return false;

        return true;
    }

    /// <summary>
    /// Convert the Unity colors over lifetime modile to SFX. Only the alpha value is used, since flyff
    /// SFX does not support color editing.
    /// </summary>
    static void SaveColors(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var colors = ps.colorOverLifetime;
        if (!colors.enabled) return;

        // We need this loop because if the color curve is just 2 keyframes -- start and end -- we need
        // the alpha to still be applied to all the existing keyframes in between.
        foreach (sfxKeyframe kf in keyframes)
        {
            float alpha = colors.color.gradient.Evaluate(FrameToTime(kf.frame, ps.main.duration)).a;
            int value = (int)(alpha * 255);
            kf.alpha = value;
        }

        foreach (GradientAlphaKey key in colors.color.gradient.alphaKeys)
        {
            int frame = TimeToFrame(key.time * ps.main.duration);

            sfxKeyframe newKeyframe = new sfxKeyframe(frame);
            sfxKeyframe nearestKf = GetNearestKeyframe(frame, keyframes);

            if (nearestKf != null)
                newKeyframe = nearestKf;
            newKeyframe.alpha = Mathf.FloorToInt(key.alpha * 255);

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.alpha = newKeyframe.alpha;
            else
                keyframes.Add(newKeyframe);
        }
    }

    /// <summary>
    /// Convert the Unity size over lifetime module to SFX (scale).
    /// </summary>
    static void SaveSizes(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var sizes = ps.sizeOverLifetime;

        // Start size matters, even if size over lifetime isn't on.
        if (!sizes.enabled)
        {
            for (int i = 0; i <= curveSubdivisions; i++)
            {
                int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
                sfxKeyframe newKeyframe = new sfxKeyframe(frame);
                newKeyframe.scale.x = newKeyframe.scale.y = newKeyframe.scale.z = ps.main.startSize.constant;

                sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
                if (keyframe != null)
                    keyframe.scale = newKeyframe.scale;
                else
                    keyframes.Add(newKeyframe);
            }
            return;
        }

        for (int i = 0; i <= curveSubdivisions; i++)
        {
            int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
            sfxKeyframe newKeyframe = new sfxKeyframe(frame);

            if (sizes.separateAxes)
            {
                float x = sizes.x.curve.Evaluate((float)(i / curveSubdivisions));
                float y = sizes.y.curve.Evaluate((float)(i / curveSubdivisions));
                float z = sizes.z.curve.Evaluate((float)(i / curveSubdivisions));
                newKeyframe.scale.x = x;
                newKeyframe.scale.y = y;
                newKeyframe.scale.z = z;
            }
            else
            {
                float value = sizes.size.curve.Evaluate((float)(i / curveSubdivisions));
                newKeyframe.scale.x = value;
                newKeyframe.scale.y = value;
                newKeyframe.scale.z = value;
            }

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.scale = newKeyframe.scale;
            else
                keyframes.Add(newKeyframe);
        }
    }

    /// <summary>
    /// Convert the Unity rotations over lifetime module to SFX (rotations).
    /// </summary>
    static void SaveRotations(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var rotations = ps.rotationOverLifetime;
        if (!rotations.enabled) return;

        for (int i = 0; i <= curveSubdivisions; i++)
        {
            int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
            sfxKeyframe newKeyframe = new sfxKeyframe(frame);

            float x, y, z;
            x = y = z = 0f;

            // TODO: If the rotation in a curve evaluates to negative, we need to start from 0 and move towards that negative, not
            // just set the rotation to the negative value.
            if (rotations.separateAxes)
            {
                if (rotations.x.mode == ParticleSystemCurveMode.Curve)
                {
                    x = rotations.xMultiplier * Mathf.Rad2Deg * rotations.x.curve.Evaluate((float)(i / curveSubdivisions));
                    y = rotations.yMultiplier * Mathf.Rad2Deg * rotations.y.curve.Evaluate((float)(i / curveSubdivisions));
                    z = rotations.zMultiplier * Mathf.Rad2Deg * rotations.z.curve.Evaluate((float)(i / curveSubdivisions));
                }
                else if (rotations.x.mode == ParticleSystemCurveMode.Constant)
                {
                    x = rotations.x.constant * (float)(i / curveSubdivisions);
                    y = rotations.y.constant * (float)(i / curveSubdivisions);
                    z = rotations.z.constant * (float)(i / curveSubdivisions);
                }
                else
                {
                    Debug.LogError("The SFX exporter only supports the 'curve' and 'constant' modes in the rotation over lifetime module.");
                    return;
                }
            }
            else  // Angular velocity
            {
                if (rotations.z.mode == ParticleSystemCurveMode.Constant)
                    z = rotations.z.constant * (float)(i / curveSubdivisions) * ps.main.duration * Mathf.Rad2Deg;   // Degrees per second
                else if (rotations.z.mode == ParticleSystemCurveMode.Curve)
                    z = rotations.z.Evaluate((float)(i / curveSubdivisions)) * Mathf.Rad2Deg;
                else
                {
                    Debug.LogError("The SFX exporter only supports the 'curve' and 'constant' modes in the rotation over lifetime module (Angular velocity).");
                    return;
                }
            }

            newKeyframe.rotation.x =  x;
            newKeyframe.rotation.y =  y;
            newKeyframe.rotation.z =  z;

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.rotation = newKeyframe.rotation;
            else
                keyframes.Add(newKeyframe);
        }
    }

    /// <summary>
    /// Simulate the same effect the noise module gives through individual keyframe positions.
    /// </summary>
    static void SaveNoise(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var noise = ps.noise;
        if (!noise.enabled) return;

        // I have to basically make my own noise-to-position system here, since there
        // is no way to get any data from the noise module in unity.
        
        for (int i = 0; i <= curveSubdivisions; i++)
        {
            int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
            float samplePoint = i / (curveSubdivisions) * noise.frequency;

            float x = Mathf.PerlinNoise(samplePoint, 0) * noise.strengthMultiplier;
            float y = Mathf.PerlinNoise(0, samplePoint) * noise.strengthMultiplier;
            float z = Mathf.PerlinNoise(samplePoint, samplePoint) * noise.strengthMultiplier;

            sfxKeyframe newKeyframe = new sfxKeyframe(frame);
            newKeyframe.position.x = x;
            newKeyframe.position.y = y;
            newKeyframe.position.z = z;

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.position += newKeyframe.position;      // We need to add here, since there are other modules that affect position
            else
                keyframes.Add(newKeyframe);
        }
    }

    /// <summary>
    /// Convert the Unity velocity over lifetime orbital velocity properties to SFX (posRotation).
    /// </summary>
    static void SaveOrbitalVelocity(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var velocity = ps.velocityOverLifetime;
        if (!velocity.enabled) return;

        float modifier = 20.0f;         // This is used to better simulate the 'velocity' in unity to 'posRotation' in SFX.

        Vector3 previousRot = new Vector3(0f, 0f, 0f);
        for (int i = 0; i <= curveSubdivisions; i++)
        {
            int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
            sfxKeyframe newKeyframe = new sfxKeyframe(frame);

            float x, y, z;
            x = y = z = 0f;
            switch (velocity.orbitalX.mode)
            {
                case ParticleSystemCurveMode.Curve:
                    x = ((velocity.orbitalX.curve.Evaluate((float)(i / curveSubdivisions)) * modifier) + previousRot.x);
                    y = ((velocity.orbitalZ.curve.Evaluate((float)(i / curveSubdivisions)) * modifier) + previousRot.y);
                    z = ((velocity.orbitalY.curve.Evaluate((float)(i / curveSubdivisions)) * modifier) + previousRot.z);
                    break;
                case ParticleSystemCurveMode.Constant:
                    x = ((velocity.orbitalX.constant) + previousRot.x);
                    y = ((velocity.orbitalZ.constant) + previousRot.y);
                    z = ((velocity.orbitalY.constant) + previousRot.z);
                    break;
                default:
                    Debug.LogError("The SFX exporter only supports the 'curve' and 'constant' modes in the velocity module (Orbital velocity).");
                    return;
            }

            previousRot = new Vector3(x, y, z);

            newKeyframe.posRotation.x = x;
            newKeyframe.posRotation.y = y;
            newKeyframe.posRotation.z = z;

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.posRotation = newKeyframe.posRotation;
            else
                keyframes.Add(newKeyframe);
        }
    }

    /// <summary>
    /// Convert the Unity velocity over lifetime module to SFX (positions).
    /// </summary>
    static void SaveVelocity(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        var velocity = ps.velocityOverLifetime;
        if (!velocity.enabled) return;

        // This modifier is used to translate unity units to flyff units and have them make more sense
        float modifier = 0.5f;

        Vector3 previousPos = new Vector3(0f, 0f, 0f);
        for (int i = 0; i <= curveSubdivisions; i++)
        {
            int frame = TimeToFrame((float)(i / curveSubdivisions) * ps.main.duration);
            sfxKeyframe newKeyframe = new sfxKeyframe(frame);

            float x, y, z;
            x = y = z = 0f;

            switch (velocity.x.mode)
            {
                case ParticleSystemCurveMode.Curve:
                    x = (velocity.x.curve.Evaluate((float)(i / curveSubdivisions)) + previousPos.x) * modifier;
                    y = (velocity.y.curve.Evaluate((float)(i / curveSubdivisions)) + previousPos.y) * modifier;
                    z = (velocity.z.curve.Evaluate((float)(i / curveSubdivisions)) + previousPos.z) * modifier;
                    break;
                case ParticleSystemCurveMode.Constant:
                    x = (velocity.x.constant + previousPos.x) * modifier;
                    y = (velocity.y.constant + previousPos.y) * modifier;
                    z = (velocity.z.constant + previousPos.z) * modifier;
                    break;
                default:
                    Debug.LogError("The SFX exporter only supports the 'curve' and 'constant' modes in the velocity module (Linear velocity).");
                    return;
            }

            previousPos = new Vector3(x, y, z);

            newKeyframe.position.x = x;
            newKeyframe.position.y = y;
            newKeyframe.position.z = z;

            sfxKeyframe keyframe = keyframes.Find(kf => kf.frame == frame);
            if (keyframe != null)
                keyframe.position += newKeyframe.position;      // We need to add here, since there are other modules that affect position
            else
                keyframes.Add(newKeyframe);
        }
    }

    static void SaveTransformPosition(ParticleSystem ps, BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        Vector3 pos = ps.transform.position;
        foreach (sfxKeyframe kf in keyframes)
            kf.position += pos;
    }

    static void WriteKeyframes(BinaryWriter file, List<sfxKeyframe> keyframes)
    {
        List<sfxKeyframe> sortedByFrame = keyframes.OrderBy(x => x.frame).ToList();
        foreach (sfxKeyframe kf in sortedByFrame)
            kf.Write(file);
    }

    static int SubdivisionToFrame(int subdivision, float max, float totalDuration)
    {
        return Mathf.FloorToInt(totalDuration * 60 * (subdivision / max));
    }

    static int TimeToFrame(float time)
    {
        return Mathf.FloorToInt(time * 60);
    }

    static float FrameToTime(int frame, float totalDuration)
    {
        return (float)(frame / (totalDuration * 60));
    }

    static sfxKeyframe GetNearestKeyframe(int frame, List<sfxKeyframe> keyframes)
    {
        sfxKeyframe keyframe = new sfxKeyframe(9999);
        int shortest = -1;
        foreach (sfxKeyframe kf in keyframes)
        {
            int distance = Mathf.Abs(frame - kf.frame);
            if (shortest == -1 || distance < shortest)
            {
                shortest = distance;
                keyframe = kf;
            }
        }

        return keyframe;
    }
}

public class sfxKeyframe
{
    // Attributes
    public int frame;
    public Vector3 position;
    public Vector3 posRotation;
    public Vector3 scale;
    public Vector3 rotation;
    public int alpha;

    public sfxKeyframe(int frame)
    {
        this.frame  = frame;
        position    = new Vector3(0f, 0f, 0f);
        posRotation = new Vector3(0f, 0f, 0f);
        scale       = new Vector3(1f, 1f, 1f);
        rotation    = new Vector3(0f, 0f, 0f);
        alpha       = 255;
    }

    /// <summary>
    /// Writes the keyframe in .sfx format to the specified file.
    /// </summary>
    public void Write(BinaryWriter file)
    {
        file.Write((ushort)frame);

        file.Write(position.x);
        file.Write(position.y);
        file.Write(position.z);

        file.Write(posRotation.x);
        file.Write(posRotation.y);
        file.Write(posRotation.z);

        file.Write(scale.x);
        file.Write(scale.y);
        file.Write(scale.z);

        file.Write(rotation.x);
        file.Write(rotation.y);
        file.Write(rotation.z);

        file.Write(alpha);
    }
}
