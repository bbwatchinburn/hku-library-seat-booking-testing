using System;
using System.Diagnostics;
using System.Threading;

namespace HkuLibrarySeatBooking
{
    internal sealed class BookingScheduler
    {
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

                    DateTime prepareAt = attempt.ReleaseAt.AddSeconds(-20);
                    if (!this.WaitUntil(prepareAt, cancellationToken))
                    {
                        return;
                    }

                    this.Publish(StatusSnapshot.Create(
                        RunState.Preparing,
                        FriendlyState.Title(RunState.Preparing),
                        "正在获取预约页面并同步服务器时间"));

                    DateTime effectiveRelease = attempt.ReleaseAt;
                    using (HkuBookingClient client = new HkuBookingClient(config, password, cookie))
                    {
                        try
                        {
                            PreparedPage page = client.Prepare(config.BookingPageUrl, cancellationToken);
                            if (page.ServerUtc.HasValue)
                            {
                                TimeSpan offset = page.ServerUtc.Value - DateTime.UtcNow;
                                if (Math.Abs(offset.TotalSeconds) >= 1)
                                {
                                    effectiveRelease = attempt.ReleaseAt.Subtract(offset);
                                }
                            }
                        }
                        catch (BookingAuthenticationException exception)
                        {
                            this.Publish(StatusSnapshot.Create(
                                RunState.NeedsAttention,
                                FriendlyState.Title(RunState.NeedsAttention),
                                exception.Message));
                            this.WaitAfterCompletedAttempt(attempt, cancellationToken);
                            continue;
                        }
                        catch (Exception exception)
                        {
                            this.Publish(StatusSnapshot.Create(
                                RunState.Failed,
                                "准备预约页面失败",
                                exception.Message));
                            this.WaitAfterCompletedAttempt(attempt, cancellationToken);
                            continue;
                        }

                        this.Publish(StatusSnapshot.Create(
                            RunState.Submitting,
                            FriendlyState.Title(RunState.Submitting),
                            "已准备就绪，等待放号时刻"));
                        if (!this.WaitUntil(effectiveRelease, cancellationToken))
                        {
                            return;
                        }
                        if (DateTime.Now > attempt.ReleaseAt.AddMinutes(2))
                        {
                            this.Publish(StatusSnapshot.Create(
                                RunState.Failed,
                                "已错过本次放号窗口",
                                "系统休眠或启动延迟超过 2 分钟，将等待下一次放号。"));
                            this.WaitAfterCompletedAttempt(attempt, cancellationToken);
                            continue;
                        }

                        int totalAttempts = config.MaxRetries + 1;
                        BookingResult finalResult = null;
                        for (int attemptNumber = 1; attemptNumber <= totalAttempts; attemptNumber++)
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
                            if (!result.Retryable || attemptNumber == totalAttempts)
                            {
                                break;
                            }

                            if (!this.Wait(config.RetryIntervalMs, cancellationToken))
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
                        this.WaitAfterCompletedAttempt(attempt, cancellationToken);
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

        private void WaitAfterCompletedAttempt(BookingAttempt attempt, CancellationToken cancellationToken)
        {
            DateTime nextPrepare = attempt.ReleaseAt.AddDays(1).AddSeconds(-20);
            if (nextPrepare <= DateTime.Now)
            {
                nextPrepare = DateTime.Now.AddMinutes(1);
            }
            if (!this.WaitUntil(nextPrepare, cancellationToken))
            {
                return;
            }

            this.Publish(StatusSnapshot.Create(
                RunState.Waiting,
                FriendlyState.Title(RunState.Waiting),
                "等待下一次放号"));
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
