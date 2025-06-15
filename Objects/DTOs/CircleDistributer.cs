using System;
using System.Collections.Generic;

namespace NetworkMonitor.DTOs;

public interface IPoint
{
    double XPosition { get; set; }
    double YPosition { get; set; }
}
public class Point
{
    public double XPosition { get; set; }
    public double YPosition { get; set; }
}

public class CircleDistributor
{
    public void DistributeIndicators(List<IPoint> indicators)
    {
        int numberOfIndicators = indicators.Count;
        double centerX = 0.5; // X-coordinate of the circle's center
        double centerY = 0.5; // Y-coordinate of the circle's center
        double maxRadius = 0.5; // Maximum radius

        if (numberOfIndicators == 1)
        {
            indicators[0].XPosition = centerX;
            indicators[0].YPosition = centerY;
        }
        else if (numberOfIndicators > 1 && numberOfIndicators < 8)
        {
            double angleStep = 2 * Math.PI / numberOfIndicators;
            for (int i = 0; i < numberOfIndicators; i++)
            {
                double angle = angleStep * i;
                indicators[i].XPosition = centerX + maxRadius * Math.Cos(angle);
                indicators[i].YPosition = centerY + maxRadius * Math.Sin(angle);
            }
        }
        else
        {
            double goldenAngle = Math.PI * (3 - Math.Sqrt(5)); // Golden angle in radians

            for (int i = 0; i < numberOfIndicators; i++)
            {
                double radius = maxRadius * Math.Sqrt((double)i / numberOfIndicators);
                double angle = i * goldenAngle;

                indicators[i].XPosition = centerX + radius * Math.Cos(angle);
                indicators[i].YPosition = centerY + radius * Math.Sin(angle);
            }
        }
    }
}

