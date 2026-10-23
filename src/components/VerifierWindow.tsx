import React, { useState, useEffect, useRef } from 'react';
import { GuardOptions, VerificationResult } from '../types';
import { verifyPassword } from '../crypto/password';
import { 
  ShieldCheck, 
  ShieldAlert, 
  Monitor, 
  User, 
  Clock, 
  RefreshCw, 
  Skull,
  Shield,
  Sparkles
} from 'lucide-react';

interface VerifierWindowProps {
  sessionId: number;
  userName: string;
  clientAddress: string;
  token: string;
  options: GuardOptions;
  onVerificationComplete: (result: VerificationResult) => void;
  onLogSecurity: (level: 'INFO' | 'WARN' | 'ERROR', action: string, details: string) => void;
  knownDemoPassword: string;
  onResurrectProcess?: () => void;
}

export const VerifierWindow: React.FC<VerifierWindowProps> = ({
  sessionId,
  userName,
  clientAddress: _clientAddress,
  token: _token,
  options,
  onVerificationComplete,
  onLogSecurity,
  knownDemoPassword,
  onResurrectProcess,
}) => {
  const [password, setPassword] = useState('');
  const [statusText, setStatusText] = useState('Oturum doğrulaması bekleniyor.');
  const [statusType, setStatusType] = useState<'muted' | 'error' | 'success'>('muted');
  const [isVerifying, setIsVerifying] = useState(false);
  const [shake, setShake] = useState(false);
  const [isRedBorder, setIsRedBorder] = useState(false);
  const [allowClose, setAllowClose] = useState(false);
  const [watchdogResurrecting, setWatchdogResurrecting] = useState(false);

  // Challenge parameters
  const [totalSeconds, setTotalSeconds] = useState(options.timeoutSeconds);
  const [remainingSeconds, setRemainingSeconds] = useState(options.timeoutSeconds);
  const [attemptsUsed, setAttemptsUsed] = useState(0);
  const maxAttempts = options.maxAttempts;
  const attemptsRemaining = Math.max(0, maxAttempts - attemptsUsed);

  // Live system clock
  const [currentTime, setCurrentTime] = useState('');
  const passwordInputRef = useRef<HTMLInputElement>(null);

  // Finished state
  const [finishedResult, setFinishedResult] = useState<VerificationResult | null>(null);

  // Live clock: dd.MM.yyyy HH:mm:ss
  useEffect(() => {
    const updateTime = () => {
      const now = new Date();
      const pad = (n: number) => n.toString().padStart(2, '0');
      const formatted = `${pad(now.getDate())}.${pad(now.getMonth() + 1)}.${now.getFullYear()} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
      setCurrentTime(formatted);
    };
    updateTime();
    const interval = setInterval(updateTime, 1000);
    return () => clearInterval(interval);
  }, []);

  // Initialize Challenge
  useEffect(() => {
    setTotalSeconds(options.timeoutSeconds);
    setRemainingSeconds(options.timeoutSeconds);
    setAttemptsUsed(0);
    setFinishedResult(null);
    setAllowClose(false);

    setStatusText('Lütfen yönetici doğrulama parolasını girin.');
    setStatusType('muted');

    if (passwordInputRef.current) {
      passwordInputRef.current.focus();
    }
  }, [sessionId, options.timeoutSeconds, options.developmentMode]);

  // Countdown timer effect
  useEffect(() => {
    if (finishedResult || allowClose || watchdogResurrecting) return;

    const timer = setInterval(() => {
      setRemainingSeconds((prev: number) => {
        if (prev <= 1) {
          clearInterval(timer);
          handleTimeout();
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [finishedResult, allowClose, watchdogResurrecting]);

  const handleTimeout = () => {
    setFinishedResult('TimedOut');
    setAllowClose(true);
    setStatusText('Süre doldu (45 sn). RDP bağlantısı kesiliyor.');
    setStatusType('error');
    triggerShakeAnimation();

    if (options.developmentMode) {
      onLogSecurity('WARN', 'TIMEOUT_DEV', `${options.timeoutSeconds} saniye doldu. [Geliştirici Modu Aktif] WTSDisconnectSession simüle edildi.`);
    } else {
      onLogSecurity('ERROR', 'TIMEOUT_DISCONNECT', `${options.timeoutSeconds} saniye doldu. WTSDisconnectSession ile RDP oturumu sonlandırıldı.`);
    }

    onVerificationComplete('TimedOut');
  };

  const triggerShakeAnimation = () => {
    setShake(true);
    setIsRedBorder(true);
    setTimeout(() => setShake(false), 300);
    setTimeout(() => setIsRedBorder(false), 700);
  };

  const handleVerify = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (finishedResult || isVerifying) return;

    if (!password.trim()) {
      setStatusText('Parola boş olamaz.');
      setStatusType('error');
      triggerShakeAnimation();
      return;
    }

    setIsVerifying(true);

    try {
      // Simulate NamedPipe IPC latency
      await new Promise((r) => setTimeout(r, 160));

      const isMatch = await verifyPassword(password, options.passwordSalt, options.passwordHash);

      if (isMatch) {
        // Success
        setFinishedResult('Success');
        setAllowClose(true);
        setStatusText('Doğrulama başarılı. Masaüstü erişimine izin verildi.');
        setStatusType('success');
        onLogSecurity('INFO', 'AUTH_SUCCESS', 'Kullanıcı doğru parolayı girdi. Masaüstü erişimine izin verildi.');
        onVerificationComplete('Success');
      } else {
        // Wrong password
        const newUsed = attemptsUsed + 1;
        setAttemptsUsed(newUsed);
        const rem = maxAttempts - newUsed;

        triggerShakeAnimation();
        setPassword('');
        if (passwordInputRef.current) {
          passwordInputRef.current.focus();
        }

        if (rem <= 0) {
          setFinishedResult('MaxAttempts');
          setAllowClose(true);
          setStatusText('3 kez hatalı parola girildi. RDP bağlantısı kesiliyor.');
          setStatusType('error');

          if (options.developmentMode) {
            onLogSecurity('ERROR', 'BRUTE_FORCE_DEV', '3 hatalı deneme sınırı aşıldı! [Geliştirici Modu] Disconnect simüle edildi.');
          } else {
            onLogSecurity('ERROR', 'BRUTE_FORCE_LOCKOUT', 'Maksimum hatalı parola sınırına ulaşıldı! WTSDisconnectSession ile kullanıcı atıldı.');
          }

          onVerificationComplete('MaxAttempts');
        } else {
          setStatusText(`Hatalı parola. Lütfen tekrar deneyin. (${rem} hak kaldı)`);
          setStatusType('error');
          onLogSecurity('WARN', 'AUTH_FAIL', `Hatalı parola denemesi yapıldı. Kullanılan: ${newUsed}/${maxAttempts}.`);
        }
      }
    } catch (err) {
      console.error(err);
      setStatusText('Doğrulama servisi hatası.');
      setStatusType('error');
    } finally {
      setIsVerifying(false);
    }
  };

  const handleTestExit = () => {
    setFinishedResult('DevExit');
    setAllowClose(true);
    setStatusText('Geliştirici Modu: Test Çıkışı ile kilit kaldırıldı.');
    setStatusType('success');
    onLogSecurity('INFO', 'DEV_MODE_EXIT', 'Geliştirici modu: Test Çıkışı butonu ile kilit sonlandırıldı.');
    onVerificationComplete('DevExit');
  };

  // Simulate process kill & Watchdog 300ms resurrection
  const handleSimulateKill = () => {
    setWatchdogResurrecting(true);
    onLogSecurity('WARN', 'PROCESS_KILLED_SIMULATION', 'LockerUI süreci (PID 4820) kullanıcı veya saldırgan tarafından taskkill ile öldürüldü!');

    setTimeout(() => {
      onLogSecurity('WARN', 'WATCHDOG_INTERVENE', 'OzgurGuard 300ms Heartbeat Watchdog süreç kapanışını tespit etti! Kilit ekranı derhal yeniden başlatılıyor...');
      setWatchdogResurrecting(false);
      triggerShakeAnimation();
      setStatusText('Saldırı Önleme: Kilit ekranı kapatılamaz! Watchdog anında yeniden başlattı.');
      setStatusType('error');
      if (onResurrectProcess) onResurrectProcess();
    }, 300);
  };

  const progressPercentage = Math.max(0, Math.min(100, (remainingSeconds / Math.max(1, totalSeconds)) * 100));

  return (
    <div className="relative min-h-[620px] w-full rounded-2xl overflow-hidden bg-gradient-to-br from-[#070A12] via-[#0B0F19] to-[#0F172A] p-4 sm:p-8 flex flex-col items-center justify-center border border-[#1E293B] shadow-2xl select-none">
      {/* Ambient background blur circles */}
      <div className="absolute top-[-100px] left-[-100px] w-[500px] h-[500px] bg-blue-600/10 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-[-100px] right-[-100px] w-[500px] h-[500px] bg-cyan-500/10 rounded-full blur-3xl pointer-events-none" />

      {/* Watchdog Simulation Bar on top */}
      <div className="w-full max-w-[540px] mb-3 flex items-center justify-between text-xs bg-slate-900/90 border border-slate-800 rounded-lg px-3 py-2 text-slate-400 z-10">
        <div className="flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
          <span>Watchdog: <strong>300ms Heartbeat Aktif</strong></span>
        </div>
        <button
          type="button"
          onClick={handleSimulateKill}
          disabled={finishedResult !== null || watchdogResurrecting}
          className="px-2.5 py-1 bg-red-950 hover:bg-red-900 text-red-300 border border-red-700/60 rounded text-[11px] font-medium flex items-center gap-1 transition-colors cursor-pointer"
          title="Kilit sürecini sonlandırmayı dene (Watchdog diriltme testi)"
        >
          <Skull className="w-3 h-3" />
          <span>Süreci Öldür (Crash Simülasyonu)</span>
        </button>
      </div>

      {/* Central Glassmorphic Security Card matching WPF MainWindow.xaml */}
      <div
        className={`w-full max-w-[540px] bg-[#111827]/90 backdrop-blur-md border ${
          isRedBorder ? 'border-red-500 shadow-red-500/20' : 'border-[#334155]'
        } rounded-2xl p-7 sm:p-9 shadow-2xl transition-all z-10 ${shake ? 'animate-shake' : ''}`}
      >
        {/* Shield Icon with glowing ring */}
        <div className="flex justify-center mb-4">
          <div className="w-16 h-16 rounded-full bg-[#1E293B] border border-blue-500/40 flex items-center justify-center shadow-lg shadow-blue-500/20">
            <Shield className="w-8 h-8 text-blue-400 fill-blue-500/20" />
          </div>
        </div>

        {/* Company & Title Header */}
        <div className="text-center">
          <h1 className="text-[20px] font-bold tracking-wide text-white uppercase font-sans">
            ÖZGÜR ZAMAN KONTROL SİSTEMLERİ
          </h1>
          <p className="mt-1.5 text-[12px] text-[#94A3B8] tracking-normal">
            Sunucu Erişim Güvenlik Katmanı | Yetkisiz Girişler Kayıt Altındadır
          </p>
        </div>

        {/* Status Label */}
        <div className="mt-5 min-h-[38px] flex items-center justify-center text-center px-2">
          <p
            className={`text-[13px] font-medium leading-snug transition-colors ${
              statusType === 'success'
                ? 'text-[#86EFAC]'
                : statusType === 'error'
                ? 'text-[#FCA5A5]'
                : 'text-[#94A3B8]'
            }`}
          >
            {statusText}
          </p>
        </div>

        {/* Password Form */}
        <form onSubmit={handleVerify} className="mt-4">
          <div className="relative">
            <input
              ref={passwordInputRef}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={finishedResult !== null || isVerifying || watchdogResurrecting}
              placeholder="Yönetici Parolası"
              className="w-full h-11 px-4 bg-[#0A0F1D] border border-[#334155] rounded-lg text-white text-[15px] focus:outline-none focus:border-[#2563EB] disabled:opacity-50 disabled:cursor-not-allowed transition-colors font-sans"
              autoFocus
            />
          </div>

          {/* Action Button */}
          <button
            type="submit"
            disabled={finishedResult !== null || isVerifying || !password || watchdogResurrecting}
            className="mt-3.5 w-full h-11 bg-[#2563EB] hover:bg-blue-600 active:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-lg font-bold text-xs tracking-wider text-white uppercase transition-colors flex items-center justify-center gap-2 cursor-pointer shadow-lg shadow-blue-600/30"
          >
            {isVerifying ? (
              <>
                <RefreshCw className="w-4 h-4 animate-spin" />
                <span>Kriptografik Doğrulama (PBKDF2)...</span>
              </>
            ) : (
              <span>GİRİŞİ DOĞRULA</span>
            )}
          </button>
        </form>

        {/* Default Password Quick-Fill Helper */}
        {knownDemoPassword && !finishedResult && (
          <div className="mt-3 flex items-center justify-between text-[11px] text-slate-400 bg-slate-900/80 px-3 py-1.5 rounded-lg border border-slate-800">
            <span>
              Varsayılan Parola: <strong className="text-blue-400 font-mono">{knownDemoPassword}</strong>
            </span>
            <button
              type="button"
              onClick={() => {
                setPassword(knownDemoPassword);
                if (passwordInputRef.current) passwordInputRef.current.focus();
              }}
              className="text-blue-400 hover:text-blue-300 font-medium underline cursor-pointer"
            >
              Otomatik Doldur
            </button>
          </div>
        )}

        {/* Timers & Counters */}
        <div className="mt-5 flex items-center justify-between text-[12px] text-[#94A3B8]">
          <span>Kalan süre: {remainingSeconds} sn</span>
          <span>Kalan deneme: {attemptsRemaining}/{maxAttempts}</span>
        </div>

        {/* Progress Bar */}
        <div className="mt-2 h-1.5 w-full bg-[#1E293B] rounded-full overflow-hidden">
          <div
            className={`h-full transition-all duration-300 ${
              remainingSeconds <= 10 ? 'bg-red-500' : 'bg-[#2563EB]'
            }`}
            style={{ width: `${progressPercentage}%` }}
          />
        </div>

        {/* System Info Footer & Safe Mode Button */}
        <div className="mt-6 pt-4 border-t border-[#334155] flex flex-wrap items-end justify-between gap-3 text-[11px] text-[#94A3B8] font-mono">
          <div className="space-y-1">
            <div className="flex items-center gap-1.5">
              <Monitor className="w-3.5 h-3.5 text-slate-500" />
              <span>Sunucu: {window.location.hostname || 'WIN-SERVER-VDS'}</span>
            </div>
            <div className="flex items-center gap-1.5">
              <User className="w-3.5 h-3.5 text-slate-500" />
              <span>Kullanıcı: {userName || 'Administrator'} (Oturum: {sessionId})</span>
            </div>
            <div className="flex items-center gap-1.5">
              <Clock className="w-3.5 h-3.5 text-slate-500" />
              <span>{currentTime || '—'}</span>
            </div>
          </div>

          {/* Development Mode: Test Exit Button */}
          {options.developmentMode && !finishedResult && (
            <button
              type="button"
              onClick={handleTestExit}
              className="px-3 py-1.5 bg-amber-950/70 hover:bg-amber-900/90 text-amber-300 border border-amber-600/40 rounded text-xs font-semibold flex items-center gap-1.5 transition-colors cursor-pointer"
              title="Geliştirici Modunda Sistem Kilidini Güvenle Kaldır"
            >
              <Sparkles className="w-3.5 h-3.5 text-amber-400" />
              <span>Test Çıkışı (Geliştirici Modu)</span>
            </button>
          )}
        </div>
      </div>

      {/* Result Outcome Banner */}
      {finishedResult && (
        <div className="mt-5 w-full max-w-[540px] z-10 animate-fadeIn">
          {finishedResult === 'Success' || finishedResult === 'DevExit' ? (
            <div className="bg-emerald-950/70 border border-emerald-500/40 text-emerald-200 p-4 rounded-xl flex items-center gap-3 shadow-lg">
              <ShieldCheck className="w-7 h-7 text-emerald-400 shrink-0" />
              <div>
                <h4 className="font-semibold text-emerald-300 text-sm">Oturum Başarıyla Doğrulandı</h4>
                <p className="text-xs text-emerald-300/80 mt-0.5">
                  Klavye kancası ve görev yöneticisi kısıtlamaları kaldırıldı. Masaüstüne tam erişim sağlandı.
                </p>
              </div>
            </div>
          ) : (
            <div className="bg-red-950/80 border border-red-500/50 text-red-200 p-4 rounded-xl flex items-center gap-3 shadow-lg">
              <ShieldAlert className="w-7 h-7 text-red-400 shrink-0" />
              <div>
                <h4 className="font-semibold text-red-300 text-sm">
                  {options.developmentMode ? 'RDP Oturum Kesme Simüle Edildi (Geliştirici Modu)' : 'RDP Oturumu Sonlandırıldı (WTSDisconnectSession)'}
                </h4>
                <p className="text-xs text-red-300/80 mt-0.5">
                  {finishedResult === 'TimedOut' && '45 saniyelik zaman aşımı süresi doldu.'}
                  {finishedResult === 'MaxAttempts' && '3 kez art arda hatalı parola denendi (Brute-force koruması).'}
                  {finishedResult === 'WindowClosed' && 'Yetkisiz pencere kapatma girişimi algılandı.'}
                </p>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
