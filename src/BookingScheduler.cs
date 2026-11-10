using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HkuLibrarySeatBooking
{
    internal sealed class BookingScheduler
    {
        private const int ClockSampleCount = 3;
        private const int ClockSampleIntervalMs = 150;

        private readonly object sync = new object();
        private readonly Action<StatusSnapshot> statusChanged;
        private Thread worker;
        private CancellationTokenSource cancellation;

        public BookingScheduler(Action<StatusSnapshot> statusChanged)
        {
            this.statusChanged = statusChanged;
        }

        public bool IsRunning
        {
            get
            {
                lock (this.sync)
                {
                    return this.worker != null && this.worker.IsAlive;
                }
            }
        }

        public void Start(BookingConfig config, string password, string cookie)
        {
            this.Stop();
            lock (this.sync)
            {
                CancellationTokenSource workerCancellation = new CancellationTokenSource();
                CancellationToken workerToken = workerCancellation.Token;
                this.cancellation = workerCancellation;
                this.worker = new Thread(() => this.Run(config.Clone(), password, cookie, workerToken));
                this.worker.IsBackground = true;
                this.worker.Name = "HKU seat booking scheduler";
                this.worker.Start();
            }
        }

        public void Stop()
        {
            Thread oldWorker;
            CancellationTokenSource oldCancellation;
            lock (this.sync)
            {
                oldWorker = this.worker;
                oldCancellation = this.cancellation;
                this.worker = null;
                this.cancellation = null;
            }

            if (oldCancellation != null)
            {
                oldCancellation.Cancel();
            }
            if (oldWorker != null && oldWorker != Thread.CurrentThread)
            {
                oldWorker.Join(1500);
            }
        }

        private void Run(
            BookingConfig config,
            string password,
            string cookie,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    BookingAttempt attempt = ReleaseSchedule.Plan(config, DateTime.Now);
                    this.Publish(StatusSnapshot.Create(
                        RunState.Waiting,
                        FriendlyState.Title(RunState.Waiting),
                        "下一次放号 " + attempt.ReleaseAt.ToString("MM-dd HH:mm") + "，目标 " + attempt.TargetDate.ToString("MM-dd ddd")));

                    DateTime recordingStartAt = attempt.ReleaseAt.AddMinutes(-2);
                    if (DateTime.Now < recordingStartAt &&
                        !this.WaitUntil(recordingStartAt, cancellationToken))
                    {
                        return;
                    }

                    using (BookingHarRecorder recorder = new BookingHarRecorder(attempt.ReleaseAt))
                    {
                        DateTime prepareAt = attempt.ReleaseAt.AddSeconds(-20);
                        if (!this.WaitUntil(prepareAt, cancellationToken))
                        {
                            return;
                        }

                        this.Publish(StatusSnapshot.Create(
                            RunState.Preparing,
                            FriendlyState.Title(RunState.Preparing),
                            "正在获取预约页面并同步服务器时间 · HAR 记录已开启"));

                        DateTime effectiveRelease = attempt.ReleaseAt;
                        ServerClockEstimate clockEstimate = new ServerClockEstimate();
                        using (HkuBookingClient client = new HkuBookingClient(
                            config,
                            password,
                            cookie,
                            recorder))
                        {
                            try
                            {
                                List<ServerClockSample> clockSamples = new List<ServerClockSample>();
                                PreparedPage page = null;
                                DateTime preparationDeadline = attempt.ReleaseAt.AddMinutes(2);
                                int preparationFailures = 0;
                                while (!cancellationToken.IsCancellationRequested)
                                {
                                if (page != null && DateTime.Now >= attempt.ReleaseAt)
                                {
                                    break;
                                }

                                try
                                {
                                    if (page == null)
                                    {
                                        page = client.Prepare(config.BookingPageUrl, cancellationToken);
                                    }
                                    else
                                    {
                                        ServerClockSample sample = client.SampleServerClock(
                                            config.BookingPageUrl,
                                            cancellationToken);
                                        if (sample != null)
                                        {
                                            clockSamples.Add(sample);
                                        }
                                    }
                                }
                                catch (BookingAuthenticationException)
                                {
                                    throw;
                                }
                                catch (Exception exception)
                                {
                                    preparationFailures++;
                                    if (DateTime.Now >= preparationDeadline)
                                    {
                                        throw;
                                    }

                                    this.Publish(StatusSnapshot.Create(
                                        RunState.Retrying,
                                        "准备预约页面",
                                        "页面连接中断，稍后重试（第 " +
                                        preparationFailures + " 次）：" + exception.Message));
                                    int retryDelayMs = BookingRetryPolicy.PreparationDelayMilliseconds(
                                        DateTime.Now,
                                        attempt.ReleaseAt);
                                    if (!this.Wait(retryDelayMs, cancellationToken))
                                    {
                                        return;
                                    }
                                    continue;
                                }

                                if (page.ClockSample != null && clockSamples.Count == 0)
                                {
                                    clockSamples.Add(page.ClockSample);
                                }

                                if (clockSamples.Count >= ClockSampleCount ||
                                    DateTime.Now >= attempt.ReleaseAt)
                                {
                                    break;
                                }

                                    if (!this.Wait(ClockSampleIntervalMs, cancellationToken))
                                    {
                                        return;
                                    }
                                }

                                clockEstimate = ServerClockCalibration.Estimate(clockSamples);
                                if (clockEstimate.HasSamples)
                                {
                                    effectiveRelease = attempt.ReleaseAt.Subtract(clockEstimate.Offset);
                                }

                                DateTime boundaryRefreshAt = effectiveRelease.AddMilliseconds(-500);
                                if (DateTime.Now < boundaryRefreshAt &&
                                    !this.WaitUntil(boundaryRefreshAt, cancellationToken))
                                {
                                    return;
                                }
                                try
                                {
                                    client.Prepare(config.BookingPageUrl, cancellationToken);
                                }
                                catch (BookingAuthenticationException)
                                {
                                    throw;
                                }
                                catch (Exception exception)
                                {
                                    this.Publish(StatusSnapshot.Create(
                                        RunState.Retrying,
                                        "准备预约页面",
                                        "放号前预刷新失败，提交阶段将继续尝试：" +
                                        exception.Message));
                                }
                            }
                            catch (BookingAuthenticationException exception)
                            {
                                this.Publish(StatusSnapshot.Create(
                                    RunState.NeedsAttention,
                                    FriendlyState.Title(RunState.NeedsAttention),
                                    exception.Message));
                                continue;
                            }
                            catch (Exception exception)
                            {
                                this.Publish(StatusSnapshot.Create(
                                    RunState.Failed,
                                    "准备预约页面失败",
                                    exception.Message));
                                continue;
                            }

                            this.Publish(StatusSnapshot.Create(
                                RunState.Submitting,
                                FriendlyState.Title(RunState.Submitting),
                                this.FormatClockReadiness(clockEstimate)));
                            if (!this.WaitUntil(effectiveRelease, cancellationToken))
                            {
                                return;
                            }
                            DateTime serverWindowNow = clockEstimate.HasSamples
                                ? DateTime.Now.Add(clockEstimate.Offset)
                                : DateTime.Now;
                            if (serverWindowNow > attempt.ReleaseAt.AddMinutes(2))
                            {
                                this.Publish(StatusSnapshot.Create(
                                    RunState.Failed,
                                    "已错过本次放号窗口",
                                    "系统休眠或启动延迟超过 2 分钟，将等待下一次放号。"));
                                continue;
                            }

                            int totalAttempts = config.MaxRetries + 1;
                            BookingResult finalResult = null;
                            DateTime submissionDeadline = effectiveRelease.AddMinutes(2);
                            for (int attemptNumber = 1; ; attemptNumber++)
                            {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            StatusSnapshot submitting = StatusSnapshot.Create(
                                attemptNumber == 1 ? RunState.Submitting : RunState.Retrying,
                                attemptNumber == 1
                                    ? FriendlyState.Title(RunState.Submitting)
                                    : FriendlyState.Title(RunState.Retrying),
                                attemptNumber == 1
                                    ? "正在向港大预约系统提交"
                                    : finalResult != null && finalResult.ConnectionFailure
                                        ? "连接失败，第 " + attemptNumber + " 次持续重试"
                                        : "第 " + attemptNumber + "/" + totalAttempts + " 次提交");
                            submitting.Attempt = attempt;
                            submitting.AttemptNumber = attemptNumber;
                            submitting.MaxAttempts = totalAttempts;
                            this.Publish(submitting);

                            BookingResult result = client.Submit(attempt, cancellationToken);
                            finalResult = result;
                            if (result.Success)
                            {
                                break;
                            }
                            if (!result.Retryable)
                            {
                                break;
                            }
                            if (!result.ConnectionFailure && attemptNumber >= totalAttempts)
                            {
                                break;
                            }
                            if (DateTime.Now >= submissionDeadline)
                            {
                                break;
                            }

                            int retryDelayMs = result.ConnectionFailure
                                ? BookingRetryPolicy.PreparationDelayMilliseconds(
                                    DateTime.Now,
                                    attempt.ReleaseAt)
                                : config.RetryIntervalMs;
                            if (!this.Wait(retryDelayMs, cancellationToken))
                            {
                                return;
                            }
                            }

                            StatusSnapshot completed;
                            if (finalResult != null && finalResult.Success)
                            {
                                completed = StatusSnapshot.Create(
                                    RunState.Success,
                                    FriendlyState.Title(RunState.Success),
                                    finalResult.Message + " · " + attempt.TargetDate.ToString("MM-dd") + " " +
                                    attempt.Slot.StartText + "–" + attempt.Slot.EndText);
                            }
                            else if (finalResult != null && finalResult.AuthenticationRequired)
                            {
                                completed = StatusSnapshot.Create(
                                    RunState.NeedsAttention,
                                    FriendlyState.Title(RunState.NeedsAttention),
                                    finalResult.Message);
                            }
                            else
                            {
                                completed = StatusSnapshot.Create(
                                    RunState.Failed,
                                    FriendlyState.Title(RunState.Failed),
                                    finalResult == null ? "没有收到预约结果。" : finalResult.Message);
                            }
                            completed.Attempt = attempt;
                            if (finalResult != null)
                            {
                                completed.Detail = finalResult.Message;
                            }
                            this.Publish(completed);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                this.Publish(StatusSnapshot.Create(
                    RunState.NeedsAttention,
                    "后台任务停止",
                    exception.Message));
            }
        }

        private bool WaitUntil(DateTime target, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan remaining = target - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                {
                    return true;
                }

                int sleepMs;
                if (remaining > TimeSpan.FromSeconds(2))
                {
                    sleepMs = 50;
                }
                else if (remaining > TimeSpan.FromMilliseconds(150))
                {
                    sleepMs = 5;
                }
                else
                {
                    sleepMs = 1;
                }

                if (!this.Wait(sleepMs, cancellationToken))
                {
                    return false;
                }
            }
            return false;
        }

        private string FormatClockReadiness(ServerClockEstimate estimate)
        {
            if (estimate == null || !estimate.HasSamples)
            {
                return "已准备就绪，等待放号时刻 · 未取得服务器时间，使用本机时钟";
            }

            return "已准备就绪，等待放号时刻 · 校时 " + estimate.SampleCount +
                " 次 · 中位往返 " + Math.Max(0, (int)Math.Round(estimate.MedianRoundTrip.TotalMilliseconds)) +
                " ms";
        }

        private bool Wait(int milliseconds, CancellationToken cancellationToken)
        {
            if (milliseconds <= 0)
            {
                return !cancellationToken.IsCancellationRequested;
            }
            return !cancellationToken.WaitHandle.WaitOne(milliseconds);
        }

        private void Publish(StatusSnapshot snapshot)
        {
            if (!string.IsNullOrEmpty(snapshot.Detail))
            {
                snapshot.Detail = snapshot.Detail.Trim();
            }
            this.statusChanged(snapshot);
        }
    }
}
