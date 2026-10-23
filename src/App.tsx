import React, { useState } from 'react';
import { GuardOptions, SecurityLogEntry, VerificationResult } from './types';
import { VerifierWindow } from './components/VerifierWindow';
import { ServiceConsole } from './components/ServiceConsole';
import { EventLogViewer } from './components/EventLogViewer';
import { SolutionExplorer } from './components/SolutionExplorer';
import { 
  Shield, 
  Terminal, 
  FileText, 
  Play, 
  FolderGit2, 
  Laptop, 
  Lock
} from 'lucide-react';

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'locker' | 'service' | 'logs' | 'source'>('locker');

  // Guard options matching appsettings.json
  const [options, setOptions] = useState<GuardOptions>({
    developmentMode: true,
    timeoutSeconds: 45,
    maxAttempts: 3,
    heartbeatIntervalMs: 300,
    passwordSalt: 'XeunT+yH3aYYp2Tx5O7L6g==',
    passwordHash: 'FmeP957MIzvl59PaEJJZ9tVawxj6rdys0eiw0j4bvWA=',
    lockerPath: 'C:\\Program Files\\OzgurGuard\\Locker\\OzgurGuard.LockerUI.exe',
    logDirectory: 'C:\\ProgramData\\OzgurGuard',
  });

  const [demoPassword, setDemoPassword] = useState('OzurGuvenlik2026!');

  // Current session simulation
  const [activeSessionId, setActiveSessionId] = useState<number>(2);
  const [userName, setUserName] = useState<string>('Administrator');
  const [clientAddress, setClientAddress] = useState<string>('192.168.1.105');
  const [sessionToken, setSessionToken] = useState<string>('E74B9C2D1F083AA5');
  const [lastResult, setLastResult] = useState<VerificationResult | null>(null);

  // Security logs matching C:\ProgramData\OzgurGuard\security.log
  const [logs, setLogs] = useState<SecurityLogEntry[]>([
    {
      id: 'log-1',
      timestamp: new Date(Date.now() - 45000).toISOString().replace('T', ' ').slice(0, 23),
      level: 'INFO',
      sessionId: 0,
      user: 'SYSTEM',
      clientIp: '127.0.0.1',
      action: 'SERVICE_STARTED',
      details: 'OzgurGuard arka plan Windows servisi Session 0\'da SYSTEM yetkisiyle başlatıldı. IIS ve Web portları engellenmez.',
    },
    {
      id: 'log-2',
      timestamp: new Date(Date.now() - 20000).toISOString().replace('T', ' ').slice(0, 23),
      level: 'INFO',
      sessionId: 2,
      user: 'Administrator',
      clientIp: '192.168.1.105',
      action: 'SESSION_ATTACH',
      details: 'RDP bağlantısı algılandı. Masaüstü kilitlendi, WH_KEYBOARD_LL klavye kancası ve DisableTaskMgr uygulandı.',
    },
  ]);

  const addSecurityLog = (level: 'INFO' | 'WARN' | 'ERROR', action: string, details: string) => {
    const newEntry: SecurityLogEntry = {
      id: Math.random().toString(36).substring(2, 9),
      timestamp: new Date().toISOString().replace('T', ' ').slice(0, 23),
      level,
      sessionId: activeSessionId,
      user: userName,
      clientIp: clientAddress,
      action,
      details,
    };
    setLogs((prev) => [newEntry, ...prev]);
  };

  const handleTriggerSession = (type: 'logon' | 'unlock' | 'console', customUser?: string, customIp?: string) => {
    const nextSessionId = activeSessionId + 1;
    const targetUser = customUser || userName;
    const targetIp = customIp || clientAddress;
    const newToken = Array.from(crypto.getRandomValues(new Uint8Array(8)))
      .map((b) => b.toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase();

    setActiveSessionId(nextSessionId);
    setUserName(targetUser);
    setClientAddress(targetIp);
    setSessionToken(newToken);
    setLastResult(null);

    const typeDesc = type === 'logon' ? 'RDP Oturumu Açıldı (SessionLogon)' : type === 'unlock' ? 'Oturum Kilidi Açıldı (SessionUnlock)' : 'Konsol Girişi';
    addSecurityLog(
      'INFO',
      'SESSION_ATTACH',
      `${typeDesc}. Oturum ${nextSessionId} için masaüstü kilitlendi ve 300ms Watchdog bekçisi başlatıldı.`
    );

    setActiveTab('locker');
  };

  return (
    <div className="min-h-screen bg-[#070A12] text-slate-100 flex flex-col font-sans">
      {/* Top Application Bar */}
      <header className="border-b border-[#1E293B] bg-[#0B0F19]/95 backdrop-blur-md sticky top-0 z-40">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-blue-600 flex items-center justify-center text-white shadow-lg shadow-blue-600/30">
              <Shield className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-base font-bold text-white tracking-tight">ÖzgürGuard</h1>
                <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-blue-950 text-blue-300 border border-blue-800">
                  v2.0 Enterprise (.NET 8)
                </span>
                {options.developmentMode && (
                  <span className="text-[11px] font-semibold px-2 py-0.5 rounded bg-amber-950/80 text-amber-300 border border-amber-800/60">
                    TEST MODU AKTİF
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-400">
                Özgür Zaman Kontrol Sistemleri &bull; Windows Server RDP & Konsol Oturum Kilit Güvenliği
              </p>
            </div>
          </div>

          {/* Quick Simulation Trigger */}
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => handleTriggerSession('logon')}
              className="px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-white text-xs font-semibold rounded-lg shadow-sm transition-colors flex items-center gap-1.5 cursor-pointer"
            >
              <Play className="w-3.5 h-3.5 fill-current" />
              <span>Yeni RDP Girişi Tetikle</span>
            </button>
          </div>
        </div>

        {/* Navigation Tabs */}
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex space-x-1 border-t border-[#1E293B]/70 overflow-x-auto">
          <button
            type="button"
            onClick={() => setActiveTab('locker')}
            className={`py-3 px-4 text-xs font-semibold border-b-2 flex items-center gap-2 transition-colors cursor-pointer whitespace-nowrap ${
              activeTab === 'locker'
                ? 'border-blue-500 text-blue-400 bg-blue-500/5'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            <Laptop className="w-4 h-4" />
            <span>Kilit Ekranı (WPF LockerUI)</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('service')}
            className={`py-3 px-4 text-xs font-semibold border-b-2 flex items-center gap-2 transition-colors cursor-pointer whitespace-nowrap ${
              activeTab === 'service'
                ? 'border-blue-500 text-blue-400 bg-blue-500/5'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            <Terminal className="w-4 h-4" />
            <span>Watchdog Servisi & Ayarlar</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('logs')}
            className={`py-3 px-4 text-xs font-semibold border-b-2 flex items-center gap-2 transition-colors cursor-pointer whitespace-nowrap ${
              activeTab === 'logs'
                ? 'border-blue-500 text-blue-400 bg-blue-500/5'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            <FileText className="w-4 h-4" />
            <span>Güvenlik Günlüğü (security.log)</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('source')}
            className={`py-3 px-4 text-xs font-semibold border-b-2 flex items-center gap-2 transition-colors cursor-pointer whitespace-nowrap ${
              activeTab === 'source'
                ? 'border-blue-500 text-blue-400 bg-blue-500/5'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            <FolderGit2 className="w-4 h-4" />
            <span>.NET 8 Kaynak Kodları & install.ps1</span>
          </button>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-4 sm:p-6 lg:p-8">
        {activeTab === 'locker' && (
          <div className="space-y-6">
            <div className="bg-[#111827] border border-[#334155] rounded-xl p-3.5 text-xs flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <div className="w-2.5 h-2.5 rounded-full bg-emerald-400 animate-pulse"></div>
                <span className="text-slate-300">
                  Kilitlenen Oturum: <strong className="text-white font-mono">#{activeSessionId}</strong> &bull;
                  Kullanıcı: <strong className="text-white">{userName}</strong> &bull;
                  İstemci IP: <strong className="text-white font-mono">{clientAddress}</strong>
                </span>
              </div>
              <div className="flex items-center gap-2 text-slate-400">
                <Lock className="w-3.5 h-3.5 text-blue-400" />
                <span>Varsayılan Şifre:</span>
                <code className="text-blue-400 bg-slate-900 px-2 py-0.5 rounded border border-slate-800 font-bold">
                  {demoPassword}
                </code>
              </div>
            </div>

            <VerifierWindow
              sessionId={activeSessionId}
              userName={userName}
              clientAddress={clientAddress}
              token={sessionToken}
              options={options}
              onVerificationComplete={(result) => setLastResult(result)}
              onLogSecurity={addSecurityLog}
              knownDemoPassword={demoPassword}
            />
          </div>
        )}

        {activeTab === 'service' && (
          <ServiceConsole
            options={options}
            onOptionsChange={setOptions}
            activeSessionId={activeSessionId}
            userName={userName}
            clientAddress={clientAddress}
            lastResult={lastResult}
            onTriggerSession={handleTriggerSession}
            onLogSecurity={addSecurityLog}
            knownDemoPassword={demoPassword}
            onUpdateDemoPassword={setDemoPassword}
          />
        )}

        {activeTab === 'logs' && (
          <EventLogViewer logs={logs} onClearLogs={() => setLogs([])} />
        )}

        {activeTab === 'source' && (
          <SolutionExplorer />
        )}
      </main>

      {/* Footer */}
      <footer className="border-t border-[#1E293B] bg-[#070A12] py-4 text-center text-xs text-slate-500 font-mono">
        Özgür Zaman Kontrol Sistemleri &bull; ÖzgürGuard Windows Server RDP Security Architecture &bull; .NET 8.0 C#
      </footer>
    </div>
  );
};
