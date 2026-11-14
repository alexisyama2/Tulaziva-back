namespace Tulaziva.Application.Ads;

public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0088;
    private const double KmPerDegreeOfLatitude = 111.195;

    /// <summary>Great-circle distance (haversine) between two points, in kilometers.</summary>
    public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var dLatitude = ToRadians(latitude2 - latitude1);
        var dLongitude = ToRadians(longitude2 - longitude1);

        var a = Math.Sin(dLatitude / 2) * Math.Sin(dLatitude / 2) +
                Math.Cos(ToRadians(latitude1)) * Math.Cos(ToRadians(latitude2)) *
                Math.Sin(dLongitude / 2) * Math.Sin(dLongitude / 2);

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>
    /// A rectangle that contains the circle of the given radius, so the database can discard most ads before the exact distance is
    /// computed. The longitude range is null when it cannot be expressed as a simple range (near a pole or across the 180th meridian).
    /// </summary>
    public static (double MinLatitude, double MaxLatitude, double? MinLongitude, double? MaxLongitude) BoundingBox(
        double latitude, double longitude, double radiusKm)
    {
        var dLatitude = radiusKm / KmPerDegreeOfLatitude;
        var minLatitude = Math.Max(-90, latitude - dLatitude);
        var maxLatitude = Math.Min(90, latitude + dLatitude);

        if (minLatitude <= -90 || maxLatitude >= 90)
            return (minLatitude, maxLatitude, null, null);

        // The degrees of longitude get shorter toward the poles; the farthest latitude of the box gives the widest range.
        var farthestLatitude = Math.Max(Math.Abs(minLatitude), Math.Abs(maxLatitude));
        var dLongitude = radiusKm / (KmPerDegreeOfLatitude * Math.Cos(ToRadians(farthestLatitude)));

        var minLongitude = longitude - dLongitude;
        var maxLongitude = longitude + dLongitude;

        if (minLongitude < -180 || maxLongitude > 180)
            return (minLatitude, maxLatitude, null, null);

        return (minLatitude, maxLatitude, minLongitude, maxLongitude);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
