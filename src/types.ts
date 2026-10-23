export interface GuardOptions {
  developmentMode: boolean;
  timeoutSeconds: number;
  maxAttempts: number;
  heartbeatIntervalMs: number;
  passwordSalt: string;
  passwordHash: string;
  lockerPath: string;
  logDirectory: string;
}

export type MessageType = 'hello' | 'challenge' | 'verify' | 'result' | 'test_exit';
export type GateAction = 'None' | 'Close' | 'Disconnect';

export interface GateMessage {
  type: MessageType;
  token?: string;
  password?: string;
  success?: boolean;
  action?: GateAction;
  message?: string;
  remainingSeconds?: number;
  attemptsRemaining?: number;
  maxAttempts?: number;
  developmentMode?: boolean;
  userName?: string;
}

export type VerificationResult =
  | 'Pending'
  | 'Success'
  | 'TimedOut'
  | 'MaxAttempts'
  | 'WindowClosed'
  | 'DevExit'
  | 'WatchdogResurrected';

export interface SecurityLogEntry {
  id: string;
  timestamp: string;
  level: 'INFO' | 'WARN' | 'ERROR';
  sessionId: number;
  user: string;
  clientIp: string;
  action: string;
  details: string;
}
