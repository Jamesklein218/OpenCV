using VideoStabilization.Models;

namespace VideoStabilization;

using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

public class VideoStabilizer
{
    private readonly int _smoothingRadius;
    private readonly double _cropRatio;
    private readonly int _maxFeatures;
    private readonly double _qualityLevel;
    private readonly double _minDistance;
    private readonly double _maxShift;
    private readonly double _maxAngle;
    private readonly int _optZoom;

    public VideoStabilizer(VideoStabilizerOptions options)
    {
        _maxFeatures = options.Detect.MaxFeatures;
        _qualityLevel = options.Detect.QualityLevel;
        _minDistance = options.Detect.MinDistance;

        _smoothingRadius = options.Transform.SmoothingRadius;
        _cropRatio = Math.Clamp(options.Transform.CropRatio, 0.0, 0.3);
        _maxShift = options.Transform.MaxShift;
        _maxAngle = options.Transform.MaxAngle;
        _optZoom = options.Transform.OptZoom;
    }

    /// <summary>
    /// Stabilizes a video file and writes the result to <paramref name="outputPath"/>.
    /// Two-pass approach: estimate all transforms, smooth globally, then warp.
    /// </summary>
    public void Stabilize(string inputPath, string outputPath)
    {
        if (!System.IO.File.Exists(inputPath))
            throw new System.IO.FileNotFoundException("Input video not found.", inputPath);

        // ── Pass 1: Estimate frame-to-frame transforms ──────────────────
        Console.WriteLine("[Pass 1] Estimating motion...");
        var transforms = EstimateMotion(inputPath, out int frameCount, out Size frameSize, out double fps);

        if (transforms.Count == 0)
            throw new InvalidOperationException("No transforms could be estimated. Video may be too short or featureless.");

        Console.WriteLine($"  Estimated {transforms.Count} transforms from {frameCount} frames.");

        // ── Compute cumulative trajectory and smooth it ──────────────────
        var trajectory = ComputeTrajectory(transforms);
        var smoothedTrajectory = SmoothTrajectory(trajectory, _smoothingRadius);
        var smoothedTransforms = RecomputeTransforms(transforms, trajectory, smoothedTrajectory);

        // Gap 3: Clamp per-frame correction to MaxShift / MaxAngle (vid.stab: maxshift, maxangle)
        if (_maxShift >= 0 || _maxAngle >= 0)
        {
            smoothedTransforms = smoothedTransforms
                .Select(t => new TransformParam(
                    _maxShift >= 0 ? Math.Clamp(t.Dx, -_maxShift, _maxShift) : t.Dx,
                    _maxShift >= 0 ? Math.Clamp(t.Dy, -_maxShift, _maxShift) : t.Dy,
                    _maxAngle >= 0 ? Math.Clamp(t.Angle, -_maxAngle, _maxAngle) : t.Angle,
                    t.LogScale))
                .ToList();
        }

        // Gap 2: Compute per-frame crop ratios (vid.stab: optzoom)
        double[] cropRatios = ComputeCropRatios(smoothedTransforms, frameSize);

        // ── Pass 2: Apply smoothed transforms and write output ───────────
        Console.WriteLine("[Pass 2] Applying stabilization...");
        ApplyStabilization(inputPath, outputPath, smoothedTransforms, cropRatios, frameSize, fps);

        Console.WriteLine($"[Done] Stabilized video saved to: {outputPath}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Motion Estimation (feature tracking with RANSAC)
    // ─────────────────────────────────────────────────────────────────────

    private List<TransformParam> EstimateMotion(
        string inputPath, out int frameCount, out Size frameSize, out double fps)
    {
        var transforms = new List<TransformParam>();

        using var capture = new VideoCapture(inputPath);
        fps = capture.Fps;
        frameSize = new Size((int)capture.Get(VideoCaptureProperties.FrameWidth),
                             (int)capture.Get(VideoCaptureProperties.FrameHeight));
        frameCount = (int)capture.Get(VideoCaptureProperties.FrameCount);

        using var prevFrame = new Mat();
        capture.Read(prevFrame);

        if (prevFrame.Empty())
            throw new InvalidOperationException("Cannot read the first frame.");

        using var prevGray = new Mat();
        Cv2.CvtColor(prevFrame, prevGray, ColorConversionCodes.BGR2GRAY);

        int processedFrames = 0;

        while (true)
        {
            using var currFrame = new Mat();
            if (!capture.Read(currFrame) || currFrame.Empty())
                break;

            using var currGray = new Mat();
            Cv2.CvtColor(currFrame, currGray, ColorConversionCodes.BGR2GRAY);

            // Detect good features in the previous frame
            var prevCorners = Cv2.GoodFeaturesToTrack(
                prevGray, _maxFeatures, _qualityLevel, _minDistance,
                null, 3, false, 0.04);

            if (prevCorners.Length < 4)
            {
                transforms.Add(new TransformParam(0, 0, 0));
                currGray.CopyTo(prevGray);
                processedFrames++;
                continue;
            }

            var prevPts = prevCorners.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray();

            var currPts = new Point2f[prevPts.Length];
            Cv2.CalcOpticalFlowPyrLK(
                prevGray, currGray, prevPts, ref currPts,
                out byte[] status, out float[] err,
                new Size(21, 21), 3, null,
                OpticalFlowFlags.None, 1e-4);

            var goodPrev = new List<Point2f>();
            var goodCurr = new List<Point2f>();
            for (int i = 0; i < status.Length; i++)
            {
                if (status[i] == 1)
                {
                    goodPrev.Add(prevPts[i]);
                    goodCurr.Add(currPts[i]);
                }
            }

            if (goodPrev.Count < 4)
            {
                transforms.Add(new TransformParam(0, 0, 0));
                currGray.CopyTo(prevGray);
                processedFrames++;
                continue;
            }

            // Estimate rigid transform (translation + rotation) with RANSAC
            using var inliersMat = new Mat();
            using var T = Cv2.EstimateAffinePartial2D(
                InputArray.Create(goodPrev.ToArray()),
                InputArray.Create(goodCurr.ToArray()),
                OutputArray.Create(inliersMat));

            if (T.Empty())
            {
                transforms.Add(new TransformParam(0, 0, 0));
            }
            else
            {
                // T is a 2×3 similarity matrix: [[s·cosθ, -s·sinθ, tx], [s·sinθ, s·cosθ, ty]]
                double dx = T.At<double>(0, 2);
                double dy = T.At<double>(1, 2);
                double angle = Math.Atan2(T.At<double>(1, 0), T.At<double>(0, 0));
                double scale = Math.Sqrt(T.At<double>(0, 0) * T.At<double>(0, 0)
                                       + T.At<double>(1, 0) * T.At<double>(1, 0));
                double logScale = Math.Log(Math.Max(scale, 1e-6));

                if (Math.Abs(dx) > frameSize.Width * 0.25 ||
                    Math.Abs(dy) > frameSize.Height * 0.25 ||
                    Math.Abs(angle) > Math.PI / 6)
                {
                    transforms.Add(new TransformParam(0, 0, 0));
                }
                else
                {
                    transforms.Add(new TransformParam(dx, dy, angle, logScale));
                }
            }

            currGray.CopyTo(prevGray);
            processedFrames++;

            if (processedFrames % 100 == 0)
                Console.WriteLine($"  Processed {processedFrames}/{frameCount - 1} frame pairs...");
        }

        return transforms;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Trajectory Computation & Smoothing
    // ─────────────────────────────────────────────────────────────────────

    private static List<Trajectory> ComputeTrajectory(List<TransformParam> transforms)
    {
        var trajectory = new List<Trajectory>(transforms.Count);
        double x = 0, y = 0, angle = 0, scale = 0;

        foreach (var t in transforms)
        {
            x += t.Dx;
            y += t.Dy;
            angle += t.Angle;
            scale += t.LogScale;
            trajectory.Add(new Trajectory(x, y, angle, scale));
        }

        return trajectory;
    }

    private static List<Trajectory> SmoothTrajectory(List<Trajectory> trajectory, int radius)
    {
        double sigma = radius / 2.0;
        var kernel = new double[2 * radius + 1];
        for (int k = 0; k < kernel.Length; k++)
        {
            double offset = k - radius;
            kernel[k] = Math.Exp(-(offset * offset) / (2 * sigma * sigma));
        }

        var smoothed = new List<Trajectory>(trajectory.Count);
        for (int i = 0; i < trajectory.Count; i++)
        {
            double sumX = 0, sumY = 0, sumAngle = 0, sumScale = 0, weightSum = 0;
            for (int j = -radius; j <= radius; j++)
            {
                int idx = Math.Clamp(i + j, 0, trajectory.Count - 1);
                double w = kernel[j + radius];
                sumX += trajectory[idx].X * w;
                sumY += trajectory[idx].Y * w;
                sumAngle += trajectory[idx].Angle * w;
                sumScale += trajectory[idx].Scale * w;
                weightSum += w;
            }
            smoothed.Add(new Trajectory(sumX / weightSum, sumY / weightSum, sumAngle / weightSum, sumScale / weightSum));
        }

        return smoothed;
    }

    private static List<TransformParam> RecomputeTransforms(
        List<TransformParam> original,
        List<Trajectory> trajectory,
        List<Trajectory> smoothedTrajectory)
    {
        var result = new List<TransformParam>(original.Count);

        for (int i = 0; i < original.Count; i++)
        {
            double diffX = smoothedTrajectory[i].X - trajectory[i].X;
            double diffY = smoothedTrajectory[i].Y - trajectory[i].Y;
            double diffAngle = smoothedTrajectory[i].Angle - trajectory[i].Angle;
            double diffLogScale = smoothedTrajectory[i].Scale - trajectory[i].Scale;

            result.Add(new TransformParam(
                original[i].Dx + diffX,
                original[i].Dy + diffY,
                original[i].Angle + diffAngle,
                original[i].LogScale + diffLogScale));
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Gap 2: Optimal zoom (vid.stab: optzoom)
    // ─────────────────────────────────────────────────────────────────────

    private double[] ComputeCropRatios(List<TransformParam> transforms, Size frameSize)
    {
        if (_optZoom == 0)
            return Enumerable.Repeat(_cropRatio, transforms.Count).ToArray();

        double ar = (double)frameSize.Height / frameSize.Width;

        double[] perFrame = transforms.Select(t =>
        {
            // Fraction of width/height lost to pure translation on each side
            double fromShiftX = Math.Abs(t.Dx) / frameSize.Width;
            double fromShiftY = Math.Abs(t.Dy) / frameSize.Height;

            // Fraction lost to rotation: the inscribed axis-aligned rect inside the
            // rotated frame has width W*cos(a) - H*|sin(a)| and height H*cos(a) - W*|sin(a)|.
            // Crop fraction per side = (1 - cos(a) + (H/W)*|sin(a)|) / 2 for X axis, etc.
            double cosA = Math.Cos(t.Angle);
            double sinA = Math.Abs(Math.Sin(t.Angle));
            double fromAngleX = (1 - cosA + ar * sinA) / 2;
            double fromAngleY = (1 - cosA + sinA / ar) / 2;

            // Scale < 1 zooms out, leaving black borders on all sides: crop = (1 - s) / 2
            double fromScale = Math.Max(0.0, (1.0 - t.Scale) / 2.0);

            return Math.Max(Math.Max(fromShiftX, fromShiftY), Math.Max(Math.Max(fromAngleX, fromAngleY), fromScale));
        }).ToArray();

        if (_optZoom == 1)
        {
            double staticCrop = Math.Min(perFrame.Max(), 0.3);
            return Enumerable.Repeat(staticCrop, transforms.Count).ToArray();
        }

        // Dynamic (OptZoom=2): per-frame, capped at 30%
        return perFrame.Select(c => Math.Min(c, 0.3)).ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Pass 2: Apply transforms and write stabilized video
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyStabilization(
        string inputPath, string outputPath,
        List<TransformParam> smoothedTransforms,
        double[] cropRatios,
        Size frameSize, double fps)
    {
        if (System.IO.File.Exists(outputPath))
            System.IO.File.Delete(outputPath);

        using var capture = new VideoCapture(inputPath);
        using var writer = new VideoWriter(
            outputPath,
            VideoWriter.FourCC('a', 'v', 'c', '1'),
            fps,
            frameSize);

        if (!writer.IsOpened())
            throw new InvalidOperationException($"Cannot open video writer for: {outputPath}");

        // Write the first frame unchanged
        using var firstFrame = new Mat();
        capture.Read(firstFrame);
        writer.Write(firstFrame);

        int frameIdx = 0;

        while (frameIdx < smoothedTransforms.Count)
        {
            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
                break;

            var t = smoothedTransforms[frameIdx];

            double s = t.Scale;
            double cosA = s * Math.Cos(t.Angle);
            double sinA = s * Math.Sin(t.Angle);

            using var transformMatrix = new Mat(2, 3, MatType.CV_64FC1);
            transformMatrix.Set(0, 0, cosA);
            transformMatrix.Set(0, 1, -sinA);
            transformMatrix.Set(0, 2, t.Dx);
            transformMatrix.Set(1, 0, sinA);
            transformMatrix.Set(1, 1, cosA);
            transformMatrix.Set(1, 2, t.Dy);

            using var stabilizedFull = new Mat();
            Cv2.WarpAffine(frame, stabilizedFull, transformMatrix, frameSize,
                InterpolationFlags.Linear, BorderTypes.Reflect);

            // Crop per-frame (size driven by OptZoom mode) then resize back to original dimensions
            double cr = cropRatios[frameIdx];
            int cropX = (int)(frameSize.Width * cr);
            int cropY = (int)(frameSize.Height * cr);
            var cropRect = new Rect(cropX, cropY,
                                    frameSize.Width - 2 * cropX,
                                    frameSize.Height - 2 * cropY);

            using var cropped = new Mat(stabilizedFull, cropRect);
            using var output = new Mat();
            Cv2.Resize(cropped, output, frameSize, 0, 0, InterpolationFlags.Linear);

            writer.Write(output);
            frameIdx++;

            if (frameIdx % 100 == 0)
                Console.WriteLine($"  Written {frameIdx}/{smoothedTransforms.Count} stabilized frames...");
        }
    }
}