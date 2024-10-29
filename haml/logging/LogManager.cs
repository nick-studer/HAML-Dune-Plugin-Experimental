/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace HamlProAppModule.haml.logging;

public static class LogManager
{
    private static readonly Dictionary<Type, ILogger> Loggers = new();
    private static string? _logPath;

    private static string? LogPath => _logPath ?? Path.Combine(Module1.AssemblyPath ?? "", @"..\..\logs\log.txt");

    // Add the below line to the beginning of a class you want to log in
    // protected  ILogger Log => LogManager.GetLogger(GetType());
    public static ILogger GetLogger(Type type)
    {
        ILogger ret;
        
        if (Loggers.ContainsKey(type))
        {
            ret = Loggers[type];
        }
        else
        {
            ret = CreateLogger(type);
            Loggers.Add(type, ret);
        }

        return ret;
    }

    private static ILogger CreateLogger(Type type)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(LogPath ?? string.Empty,
                shared: true,
                rollingInterval: RollingInterval.Day, 
                retainedFileCountLimit: 31,
                outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}::{MemberName} {Message}{NewLine}{Exception}"
            )
            .CreateLogger().ForContext(type);
    }
}
