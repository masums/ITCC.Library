﻿using System;
using System.Diagnostics;
using ITCC.Logging.Interfaces;

namespace ITCC.Logging.Loggers
{
    /// <summary>
    ///     Simple logger for debug logging (via System.Diagnostics.Debug)
    /// </summary>
    public class DebugLogger : ILogReceiver
    {
        #region ILogReceiver
        public LogLevel Level { get; set; }

        public virtual void WriteEntry(object sender, LogEntryEventArgs args)
        {
            if (args.Level > Level)
                return;

            Debug.WriteLine(args);
        }
        #endregion

        #region public
        public DebugLogger()
        {
            Level = Logger.Level;
        }

        public DebugLogger(LogLevel level)
        {
            Level = level;
        }
        #endregion
    }
}