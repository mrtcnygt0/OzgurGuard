import React, { useState } from 'react';
import { GuardOptions, VerificationResult } from '../types';
import { createHash } from '../crypto/password';
import { 
  Server, 
  Key, 
  Settings, 
  Play, 
  ShieldCheck, 
  Terminal, 
  Save, 
  RefreshCw, 
  Lock,
  Copy,
  Check
} from 'lucide-react';

interface ServiceConsoleProps {
  options: GuardOptions;
  onOptionsChange: (newOptions: GuardOptions) => void;
  activeSessionId: number;
  userName: string;
  clientAddress: string;
  lastResult: VerificationResult | null;
  onTriggerSession: (type: 'logon' | 'unlock' | 'console', user?: string, ip?: string) => void;
  onLogSecurity: (level: 'INFO' | 'WARN' | 'ERROR', action: string, details: string) => void;
  knownDemoPassword: string;
  onUpdateDemoPassword: (pwd: string) => void;
}

export const ServiceConsole: React.FC<ServiceConsoleProps> = ({
  options,
  onOptionsChange,
  activeSessionId,
  userName: _userName,
  clientAddress: _clientAddress,
  lastResult,
  onTriggerSession,
  onLogSecurity,
  knownDemoPassword,
  onUpdateDemoPassword,
}) => {
  // Config form state
  const [developmentMode, setDevelopmentMode] = useState(options.developmentMode);
  const [timeoutSeconds, setTimeoutSeconds] = useState(options.timeoutSeconds);
  const [maxAttempts, setMaxAttempts] = useState(options.maxAttempts);
  const [heartbeatIntervalMs, setHeartbeatIntervalMs] = useState(options.heartbeatIntervalMs);

  // Password Tool state (set-password CLI emulator)
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordToolStatus, setPasswordToolStatus] = useState<string | null>(null);
  const [isHashing, setIsHashing] = useState(false);
  const [copiedField, setCopiedField] = useState<string | null>(null);

  // Custom session trigger modal state
  const [customUser, setCustomUser] = useState('Administrator');
  const [customIp, setCustomIp] = useState('192.168.1.105');

  const handleSaveOptions = () => {
    const updated: GuardOptions = {
      ...options,
      developmentMode,
      timeoutSeconds,
      maxAttempts,
      heartbeatIntervalMs,
    };
    onOptionsChange(updated);
    onLogSecurity(
      'INFO',
      'CONFIG_UPDATED',
      `OzgurGuard ayarları güncellendi. DevMode=${developmentMode}, Timeout=${timeoutSeconds}s, MaxAttempts=${maxAttempts}, Heartbeat=${heartbeatIntervalMs}ms`
    );
  };

  const handleSetPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newPassword) {
      setPasswordToolStatus('Boş parola kabul edilmez.');
      return;
    }
    if (newPassword !== confirmPassword) {
      setPasswordToolStatus('Parolalar eşleşmedi.');
      return;
    }

    setIsHashing(true);
    setPasswordToolStatus('PBKDF2 (SHA-256, 100.000 iterasyon) türetiliyor...');

    try {
      const { saltBase64, hashBase64 } = await createHash(newPassword);

      const updated: GuardOptions = {
        ...options,
        passwordSalt: saltBase64,
        passwordHash: hashBase64,
      };

      onOptionsChange(updated);
      onUpdateDemoPassword(newPassword);
      setNewPassword('');
      setConfirmPassword('');
      setPasswordToolStatus('Parola başarıyla güncellendi (appsettings.json yazıldı).');
      onLogSecurity('INFO', 'PASSWORD_CHANGED', 'Yeni PBKDF2 salt ve hash türetilerek appsettings.json güncellendi.');
    } catch (err) {
      setPasswordToolStatus(`Hata oluştu: ${(err as Error).message}`);
    } finally {
      setIsHashing(false);
    }
  };

  const copyToClipboard = (text: string, field: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  return (
    <div className="space-y-6 text-slate-200">
      {/* Windows Service Status Bar */}
      <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3.5">
          <div className="w-10 h-10 rounded-lg bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400">
            <Server className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h2 className="font-semibold text-white text-base">OzgurGuard.Service (Windows Service)</h2>
              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-950/80 text-emerald-400 border border-emerald-500/30">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
                NT AUTHORITY\SYSTEM (Session 0)
              </span>
            </div>
            <p className="text-xs text-slate-400 mt-0.5">
              300ms Watchdog Bekçisi Aktif &bull; WTS Oturum Olay İzleyicisi Devrede &bull; IIS/Web Portlarına Dokunmaz
            </p>
          </div>
        </div>

        {/* Quick Simulator Session Trigger */}
        <div className="flex items-center gap-2 flex-wrap">
          <button
            type="button"
            onClick={() => onTriggerSession('logon', customUser, customIp)}
            className="px-3.5 py-2 bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-white text-xs font-semibold rounded-lg transition-colors flex items-center gap-1.5 cursor-pointer shadow-sm"
          >
            <Play className="w-3.5 h-3.5 fill-current" />
            <span>RDP Girişi Simüle Et</span>
          </button>
          <button
            type="button"
            onClick={() => onTriggerSession('unlock', customUser, customIp)}
            className="px-3 py-2 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium rounded-lg border border-slate-700 transition-colors flex items-center gap-1.5 cursor-pointer"
          >
            <Lock className="w-3.5 h-3.5" />
            <span>Kilit Açma (Unlock)</span>
          </button>
          <button
            type="button"
            onClick={() => onTriggerSession('console', 'ConsoleAdmin', '127.0.0.1')}
            className="px-3 py-2 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium rounded-lg border border-slate-700 transition-colors flex items-center gap-1.5 cursor-pointer"
          >
            <Terminal className="w-3.5 h-3.5" />
            <span>Konsol Modu (--console)</span>
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Guard Configuration (appsettings.json) */}
        <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg">
          <div className="flex items-center justify-between border-b border-[#1E293B] pb-3 mb-4">
            <div className="flex items-center gap-2 text-white font-semibold text-sm">
              <Settings className="w-4 h-4 text-blue-400" />
              <span>Güvenlik Yapılandırması (appsettings.json)</span>
            </div>
            <span className="text-[11px] font-mono text-slate-400 bg-slate-800/80 px-2 py-0.5 rounded border border-slate-700">
              OzgurGuard:Config
            </span>
          </div>

          <div className="space-y-4 text-xs">
            {/* Development Mode Switch */}
            <div className="p-3.5 rounded-lg bg-slate-900/70 border border-slate-800 flex items-start justify-between gap-3">
              <div>
                <span className="font-semibold text-white block text-sm">Geliştirici Modu (DevelopmentMode)</span>
                <p className="text-slate-400 mt-1 leading-relaxed text-[11px]">
                  <strong>True</strong> iken ekranda &quot;Test Çıkışı&quot; butonu belirir ve kendimizi kitlememizi önler. Canlı sunucuya kurulurken <strong>False</strong> yapılmalıdır.
                </p>
              </div>
              <button
                type="button"
                onClick={() => setDevelopmentMode(!developmentMode)}
                className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none ${
                  developmentMode ? 'bg-amber-500' : 'bg-slate-700'
                }`}
              >
                <span
                  className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow-lg ring-0 transition duration-200 ease-in-out ${
                    developmentMode ? 'translate-x-5' : 'translate-x-0'
                  }`}
                />
              </button>
            </div>

            {/* Timeout Slider */}
            <div>
              <div className="flex justify-between items-center mb-1.5">
                <label className="font-medium text-slate-300">Zaman Aşımı (TimeoutSeconds):</label>
                <span className="font-mono text-blue-400 font-bold">{timeoutSeconds} saniye</span>
              </div>
              <input
                type="range"
                min="10"
                max="120"
                step="5"
                value={timeoutSeconds}
                onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
                className="w-full h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-blue-600"
              />
              <span className="text-[11px] text-slate-500 mt-1 block">
                RDP oturumu açıldıktan sonra 45 sn içinde doğru parola girilmezse WTSDisconnectSession ile atılır.
              </span>
            </div>

            {/* Max Attempts Slider */}
            <div>
              <div className="flex justify-between items-center mb-1.5">
                <label className="font-medium text-slate-300">Maksimum Hatalı Deneme (MaxAttempts):</label>
                <span className="font-mono text-blue-400 font-bold">{maxAttempts} deneme</span>
              </div>
              <input
                type="range"
                min="1"
                max="10"
                step="1"
                value={maxAttempts}
                onChange={(e) => setMaxAttempts(Number(e.target.value))}
                className="w-full h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-blue-600"
              />
              <span className="text-[11px] text-slate-500 mt-1 block">
                Art arda 3 kez hatalı şifre girilirse brute-force koruması RDP oturumunu anında koparır.
              </span>
            </div>

            {/* Watchdog Heartbeat Interval */}
            <div>
              <div className="flex justify-between items-center mb-1.5">
                <label className="font-medium text-slate-300">Watchdog Denetim Aralığı (Heartbeat):</label>
                <span className="font-mono text-emerald-400 font-bold">{heartbeatIntervalMs} ms</span>
              </div>
              <input
                type="range"
                min="100"
                max="1000"
                step="50"
                value={heartbeatIntervalMs}
                onChange={(e) => setHeartbeatIntervalMs(Number(e.target.value))}
                className="w-full h-1.5 bg-slate-800 rounded-lg appearance-none cursor-pointer accent-emerald-500"
              />
              <span className="text-[11px] text-slate-500 mt-1 block">
                Servis her 300ms&apos;de bir kilit ekranının hayatta olduğunu doğrular; öldürülürse hemen diriltir.
              </span>
            </div>

            {/* Save Config Button */}
            <div className="pt-2">
              <button
                type="button"
                onClick={handleSaveOptions}
                className="w-full py-2 bg-slate-800 hover:bg-slate-700 active:bg-slate-600 text-white rounded-lg border border-slate-700 font-medium transition-colors flex items-center justify-center gap-2 cursor-pointer"
              >
                <Save className="w-3.5 h-3.5" />
                <span>Yapılandırmayı Kaydet ve Servise Uygula</span>
              </button>
            </div>
          </div>
        </div>

        {/* PasswordTool CLI & Crypto Manager */}
        <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between border-b border-[#1E293B] pb-3 mb-4">
              <div className="flex items-center gap-2 text-white font-semibold text-sm">
                <Key className="w-4 h-4 text-emerald-400" />
                <span>Parola Yönetim Aracı (set-password CLI)</span>
              </div>
              <span className="text-[11px] font-mono text-slate-400 bg-slate-800/80 px-2 py-0.5 rounded border border-slate-700">
                PBKDF2 SHA-256
              </span>
            </div>

            <p className="text-xs text-slate-400 mb-3 leading-relaxed">
              Düz parola diske asla kaydedilmez. Rastgele 16 bayt salt ile <strong>100.000 iterasyon</strong> PBKDF2 türetilerek saklanır.
            </p>

            <form onSubmit={handleSetPassword} className="space-y-3 text-xs">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="block text-slate-300 font-medium mb-1">Yeni Parola:</label>
                  <input
                    type="password"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    placeholder="Yeni kilit parolası"
                    className="w-full px-3 py-2 bg-slate-900 border border-slate-700 rounded-lg text-white text-xs focus:outline-none focus:border-blue-500"
                  />
                </div>
                <div>
                  <label className="block text-slate-300 font-medium mb-1">Parola (Tekrar):</label>
                  <input
                    type="password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Parolayı onaylayın"
                    className="w-full px-3 py-2 bg-slate-900 border border-slate-700 rounded-lg text-white text-xs focus:outline-none focus:border-blue-500"
                  />
                </div>
              </div>

              <button
                type="submit"
                disabled={isHashing || !newPassword}
                className="w-full py-2 bg-emerald-600 hover:bg-emerald-500 active:bg-emerald-700 disabled:opacity-50 text-white rounded-lg font-medium transition-colors flex items-center justify-center gap-2 cursor-pointer"
              >
                {isHashing ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Hash Hesaplanıyor (100.000 iterasyon)...</span>
                  </>
                ) : (
                  <>
                    <Key className="w-3.5 h-3.5" />
                    <span>Parolayı Güncelle (set-password)</span>
                  </>
                )}
              </button>

              {passwordToolStatus && (
                <div className="p-2.5 rounded bg-slate-900/90 border border-slate-700 text-xs text-slate-300">
                  {passwordToolStatus}
                </div>
              )}
            </form>
          </div>

          {/* Active PBKDF2 Values Preview */}
          <div className="mt-4 pt-3 border-t border-slate-800 space-y-2 text-[11px] font-mono">
            <div className="flex items-center justify-between text-slate-400">
              <span>Aktif Parola:</span>
              <span className="text-emerald-400 font-bold bg-slate-900 px-2 py-0.5 rounded border border-slate-800">
                {knownDemoPassword || 'OzurGuvenlik2026!'}
              </span>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span className="truncate pr-2">Salt (Base64):</span>
              <div className="flex items-center gap-1.5">
                <span className="text-slate-300 bg-slate-900 px-1.5 py-0.5 rounded max-w-[150px] truncate">
                  {options.passwordSalt}
                </span>
                <button
                  type="button"
                  onClick={() => copyToClipboard(options.passwordSalt, 'salt')}
                  className="p-1 hover:text-white"
                >
                  {copiedField === 'salt' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                </button>
              </div>
            </div>
            <div className="flex items-center justify-between text-slate-400">
              <span className="truncate pr-2">Hash (Base64):</span>
              <div className="flex items-center gap-1.5">
                <span className="text-slate-300 bg-slate-900 px-1.5 py-0.5 rounded max-w-[150px] truncate">
                  {options.passwordHash}
                </span>
                <button
                  type="button"
                  onClick={() => copyToClipboard(options.passwordHash, 'hash')}
                  className="p-1 hover:text-white"
                >
                  {copiedField === 'hash' ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Active Session & SessionGate Monitor */}
      <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg">
        <div className="flex items-center justify-between border-b border-[#1E293B] pb-3 mb-4">
          <div className="flex items-center gap-2 text-white font-semibold text-sm">
            <ShieldCheck className="w-4 h-4 text-blue-400" />
            <span>Aktif Oturum Kapısı Durumu (WTS Session Interop)</span>
          </div>
          <span className="text-xs text-slate-400">
            Hedef Oturum ID: <strong className="text-white font-mono">{activeSessionId}</strong>
          </span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-4 gap-4 text-xs">
          <div className="bg-slate-900/60 p-3 rounded-lg border border-slate-800">
            <span className="text-slate-400 block mb-1">Kullanıcı Adı:</span>
            <input
              type="text"
              value={customUser}
              onChange={(e) => setCustomUser(e.target.value)}
              className="w-full bg-slate-950 border border-slate-700 rounded px-2 py-1 text-white font-mono text-xs focus:outline-none"
            />
          </div>

          <div className="bg-slate-900/60 p-3 rounded-lg border border-slate-800">
            <span className="text-slate-400 block mb-1">İstemci IP Adresi:</span>
            <input
              type="text"
              value={customIp}
              onChange={(e) => setCustomIp(e.target.value)}
              className="w-full bg-slate-950 border border-slate-700 rounded px-2 py-1 text-white font-mono text-xs focus:outline-none"
            />
          </div>

          <div className="bg-slate-900/60 p-3 rounded-lg border border-slate-800">
            <span className="text-slate-400 block mb-1">Protokol İletişimi:</span>
            <span className="text-slate-200 font-mono flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-emerald-400"></span>
              NamedPipe \\.\pipe\OzgurGuard-{activeSessionId}
            </span>
          </div>

          <div className="bg-slate-900/60 p-3 rounded-lg border border-slate-800">
            <span className="text-slate-400 block mb-1">Son Doğrulama Durumu:</span>
            <span
              className={`font-semibold font-mono ${
                lastResult === 'Success' || lastResult === 'DevExit'
                  ? 'text-emerald-400'
                  : lastResult
                  ? 'text-red-400'
                  : 'text-amber-400'
              }`}
            >
              {lastResult ? lastResult : 'Doğrulama Bekliyor'}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
