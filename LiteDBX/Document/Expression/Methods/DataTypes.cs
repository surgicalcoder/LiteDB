using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDbX;

internal partial class BsonExpressionMethods
{
    #region NEW_INSTANCE

    /// <summary>
    /// Return a new instance of MINVALUE
    /// </summary>
    public static BsonValue MINVALUE()
    {
        return BsonValue.MinValue;
    }

    /// <summary>
    /// Create a new OBJECTID value
    /// </summary>
    [Volatile]
    public static BsonValue OBJECTID()
    {
        return ObjectId.NewObjectId();
    }

    /// <summary>
    /// Create a new GUID value
    /// </summary>
    [Volatile]
    public static BsonValue GUID()
    {
        return Guid.NewGuid();
    }

    /// <summary>
    /// Return a new DATETIME (Now)
    /// </summary>
    [Volatile]
    public static BsonValue NOW()
    {
        return DateTime.Now;
    }

    /// <summary>
    /// Return a new DATETIME (UtcNow)
    /// </summary>
    [Volatile]
    public static BsonValue NOW_UTC()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Return a new DATETIME (Today)
    /// </summary>
    [Volatile]
    public static BsonValue TODAY()
    {
        return DateTime.Today;
    }

    /// <summary>
    /// Return a new instance of MAXVALUE
    /// </summary>
    public static BsonValue MAXVALUE()
    {
        return BsonValue.MaxValue;
    }

    #endregion

    #region DATATYPE

    // ==> MaxValue is a constant
    // ==> "null" are a keyword

    /// <summary>
    /// Convert values into INT32. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue INT32(BsonValue value)
    {
        if (value.IsNumber)
        {
            return value.AsInt32;
        }

        if (value.IsString)
        {
            if (int.TryParse(value.AsString, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into INT64. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue INT64(BsonValue value)
    {
        if (value.IsNumber)
        {
            return value.AsInt64;
        }

        if (value.IsString)
        {
            if (long.TryParse(value.AsString, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DOUBLE. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DOUBLE(Collation collation, BsonValue value)
    {
        if (value.IsNumber)
        {
            return value.AsDouble;
        }

        if (value.IsString)
        {
            if (double.TryParse(value.AsString, NumberStyles.Any, collation.Culture.NumberFormat, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DOUBLE. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DOUBLE(BsonValue value, BsonValue culture)
    {
        if (value.IsNumber)
        {
            return value.AsDouble;
        }

        if (value.IsString && culture.IsString)
        {
            var c = new CultureInfo(culture.AsString); // en-US

            if (double.TryParse(value.AsString, NumberStyles.Any, c.NumberFormat, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DECIMAL. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DECIMAL(Collation collation, BsonValue value)
    {
        if (value.IsNumber)
        {
            return value.AsDecimal;
        }

        if (value.IsString)
        {
            if (decimal.TryParse(value.AsString, NumberStyles.Any, collation.Culture.NumberFormat, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DECIMAL. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DECIMAL(BsonValue value, BsonValue culture)
    {
        if (value.IsNumber)
        {
            return value.AsDecimal;
        }

        if (value.IsString && culture.IsString)
        {
            var c = new CultureInfo(culture.AsString); // en-US

            if (decimal.TryParse(value.AsString, NumberStyles.Any, c.NumberFormat, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert value into STRING
    /// </summary>
    public static BsonValue STRING(BsonValue value)
    {
        return
            value.IsNull ? "" :
            value.IsString ? value.AsString :
            value.ToString();
    }

    // ==> there is no convert to BsonDocument, must use { .. } syntax 

    /// <summary>
    /// Return an array from list of values. Support multiple values but returns a single value
    /// </summary>
    public static BsonValue ARRAY(IEnumerable<BsonValue> values)
    {
        return new BsonArray(values);
    }

    /// <summary>
    /// Return an binary from string (base64) values
    /// </summary>
    public static BsonValue BINARY(BsonValue value)
    {
        if (value.IsBinary)
        {
            return value;
        }

        if (value.IsString)
        {
            byte[] data = null;
            var isBase64 = false;

            try
            {
                data = Convert.FromBase64String(value.AsString);
                isBase64 = true;
            }
            catch (FormatException) { }

            if (isBase64)
            {
                return data;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into OBJECTID. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue OBJECTID(BsonValue value)
    {
        if (value.IsObjectId)
        {
            return value.AsObjectId;
        }

        if (value.IsString)
        {
            ObjectId val = null;
            var isObjectId = false;

            try
            {
                val = new ObjectId(value.AsString);
                isObjectId = true;
            }
            catch { }

            if (isObjectId)
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into GUID. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue GUID(BsonValue value)
    {
        if (value.IsGuid)
        {
            return value.AsGuid;
        }

        if (value.IsString)
        {
            var val = Guid.Empty;
            var isGuid = false;

            try
            {
                val = new Guid(value.AsString);
                isGuid = true;
            }
            catch { }

            if (isGuid)
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Return converted value into BOOLEAN value
    /// </summary>
    public static BsonValue BOOLEAN(BsonValue value)
    {
        if (value.IsBoolean)
        {
            return value.AsBoolean;
        }

        var val = false;
        var isBool = false;

        try
        {
            val = Convert.ToBoolean(value.AsString);
            isBool = true;
        }
        catch { }

        if (isBool)
        {
            return val;
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DATETIME. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DATETIME(Collation collation, BsonValue value)
    {
        if (value.IsDateTime)
        {
            return value.AsDateTime;
        }

        if (value.IsString)
        {
            if (DateTime.TryParse(value.AsString, collation.Culture.DateTimeFormat, DateTimeStyles.None, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DATETIME. Returns empty if not possible to convert. Support custom culture info
    /// </summary>
    public static BsonValue DATETIME(BsonValue value, BsonValue culture)
    {
        if (value.IsDateTime)
        {
            return value.AsDateTime;
        }

        if (value.IsString && culture.IsString)
        {
            var c = new CultureInfo(culture.AsString); // en-US

            if (DateTime.TryParse(value.AsString, c.DateTimeFormat, DateTimeStyles.None, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DATETIME. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DATETIME_UTC(Collation collation, BsonValue value)
    {
        if (value.IsDateTime)
        {
            return value.AsDateTime;
        }

        if (value.IsString)
        {
            if (DateTime.TryParse(value.AsString, collation.Culture.DateTimeFormat, DateTimeStyles.AssumeUniversal, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Convert values into DATETIME. Returns empty if not possible to convert
    /// </summary>
    public static BsonValue DATETIME_UTC(BsonValue value, BsonValue culture)
    {
        if (value.IsDateTime)
        {
            return value.AsDateTime;
        }

        if (value.IsString && culture.IsString)
        {
            var c = new CultureInfo(culture.AsString); // en-US

            if (DateTime.TryParse(value.AsString, c.DateTimeFormat, DateTimeStyles.AssumeUniversal, out var val))
            {
                return val;
            }
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Create a new instance of DATETIME based on year, month, day (local time)
    /// </summary>
    public static BsonValue DATETIME(BsonValue year, BsonValue month, BsonValue day)
    {
        if (year.IsNumber && month.IsNumber && day.IsNumber)
        {
            return new DateTime(year.AsInt32, month.AsInt32, day.AsInt32);
        }

        return BsonValue.Null;
    }

    /// <summary>
    /// Create a new instance of DATETIME based on year, month, day (UTC)
    /// </summary>
    public static BsonValue DATETIME_UTC(BsonValue year, BsonValue month, BsonValue day)
    {
        if (year.IsNumber && month.IsNumber && day.IsNumber)
        {
            return new DateTime(year.AsInt32, month.AsInt32, day.AsInt32, 0, 0, 0, DateTimeKind.Utc);
        }

        return BsonValue.Null;
    }

    #endregion

    #region IS_DATETYPE

    /// <summary>
    /// Return true if value is MINVALUE
    /// </summary>
    public static BsonValue IS_MINVALUE(BsonValue value)
    {
        return value.IsMinValue;
    }

    /// <summary>
    /// Return true if value is NULL
    /// </summary>
    public static BsonValue IS_NULL(BsonValue value)
    {
        return value.IsNull;
    }

    /// <summary>
    /// Return true if value is INT32
    /// </summary>
    public static BsonValue IS_INT32(BsonValue value)
    {
        return value.IsInt32;
    }

    /// <summary>
    /// Return true if value is INT64
    /// </summary>
    public static BsonValue IS_INT64(BsonValue value)
    {
        return value.IsInt64;
    }

    /// <summary>
    /// Return true if value is DOUBLE
    /// </summary>
    public static BsonValue IS_DOUBLE(BsonValue value)
    {
        return value.IsDouble;
    }

    /// <summary>
    /// Return true if value is DECIMAL
    /// </summary>
    public static BsonValue IS_DECIMAL(BsonValue value)
    {
        return value.IsDecimal;
    }

    /// <summary>
    /// Return true if value is NUMBER (int, double, decimal)
    /// </summary>
    public static BsonValue IS_NUMBER(BsonValue value)
    {
        return value.IsNumber;
    }

    /// <summary>
    /// Return true if value is STRING
    /// </summary>
    public static BsonValue IS_STRING(BsonValue value)
    {
        return value.IsString;
    }

    /// <summary>
    /// Return true if value is DOCUMENT
    /// </summary>
    public static BsonValue IS_DOCUMENT(BsonValue value)
    {
        return value.IsDocument;
    }

    /// <summary>
    /// Return true if value is ARRAY
    /// </summary>
    public static BsonValue IS_ARRAY(BsonValue value)
    {
        return value.IsArray;
    }

    /// <summary>
    /// Return true if value is BINARY
    /// </summary>
    public static BsonValue IS_BINARY(BsonValue value)
    {
        return value.IsBinary;
    }

    /// <summary>
    /// Return true if value is OBJECTID
    /// </summary>
    public static BsonValue IS_OBJECTID(BsonValue value)
    {
        return value.IsObjectId;
    }

    /// <summary>
    /// Return true if value is GUID
    /// </summary>
    public static BsonValue IS_GUID(BsonValue value)
    {
        return value.IsGuid;
    }

    /// <summary>
    /// Return true if value is BOOLEAN
    /// </summary>
    public static BsonValue IS_BOOLEAN(BsonValue value)
    {
        return value.IsBoolean;
    }

    /// <summary>
    /// Return true if value is DATETIME
    /// </summary>
    public static BsonValue IS_DATETIME(BsonValue value)
    {
        return value.IsDateTime;
    }

    /// <summary>
    /// Return true if value is DATE (alias to DATETIME)
    /// </summary>
    public static BsonValue IS_MAXVALUE(BsonValue value)
    {
        return value.IsMaxValue;
    }

    #endregion

    #region ALIAS

    /// <summary>
    /// Alias to INT32(values)
    /// </summary>
    public static BsonValue INT(BsonValue value)
    {
        return INT32(value);
    }

    /// <summary>
    /// Alias to INT64(values)
    /// </summary>
    public static BsonValue LONG(BsonValue value)
    {
        return INT64(value);
    }

    /// <summary>
    /// Alias to BOOLEAN(values)
    /// </summary>
    public static BsonValue BOOL(BsonValue value)
    {
        return BOOLEAN(value);
    }

    /// <summary>
    /// Alias to DATETIME(values) and DATETIME_UTC(values)
    /// </summary>
    public static BsonValue DATE(Collation collation, BsonValue value)
    {
        return DATETIME(collation, value);
    }

    public static BsonValue DATE(BsonValue values, BsonValue culture)
    {
        return DATETIME(values, culture);
    }

    public static BsonValue DATE_UTC(Collation collation, BsonValue value)
    {
        return DATETIME_UTC(collation, value);
    }

    public static BsonValue DATE_UTC(BsonValue values, BsonValue culture)
    {
        return DATETIME_UTC(values, culture);
    }

    public static BsonValue DATE(BsonValue year, BsonValue month, BsonValue day)
    {
        return DATETIME(year, month, day);
    }

    public static BsonValue DATE_UTC(BsonValue year, BsonValue month, BsonValue day)
    {
        return DATETIME_UTC(year, month, day);
    }

    /// <summary>
    /// Alias to IS_INT32(values)
    /// </summary>
    public static BsonValue IS_INT(BsonValue value)
    {
        return IS_INT32(value);
    }

    /// <summary>
    /// Alias to IS_INT64(values)
    /// </summary>
    public static BsonValue IS_LONG(BsonValue value)
    {
        return IS_INT64(value);
    }

    /// <summary>
    /// Alias to IS_BOOLEAN(values)
    /// </summary>
    public static BsonValue IS_BOOL(BsonValue value)
    {
        return IS_BOOLEAN(value);
    }

    /// <summary>
    /// Alias to IS_DATE(values)
    /// </summary>
    public static BsonValue IS_DATE(BsonValue value)
    {
        return IS_DATETIME(value);
    }

    #endregion
}