using System.Diagnostics;
using Coravel.Scheduling.Schedule.Interfaces;

namespace SolisManager.Extensions;

public static class SchedulerExtensions
{
    public static IScheduledEventConfiguration RunAtStartupIfDebugging(this IScheduledEventConfiguration scheduleConfig)
    {
        if (Debugger.IsAttached)
            scheduleConfig.RunOnceAtStart();
        
        return scheduleConfig;
    }

}