export interface VideoItem {
  id: string;
  filename: string;
  channelId: string;
  date: string;
  startTime: string;
  endTime: string;
  hasCachedH264: boolean;
  hasDetections: boolean;
}

export interface ChannelGroup {
  channelId: string;
  videos: VideoItem[];
}

export interface ConversionStatus {
  status: 'not_started' | 'converting' | 'ready' | 'error';
  progress: number;
}

export interface DetectionEvent {
  timestampMs: number;
  durationMs: number;
  regions: Array<{
    x: number;
    y: number;
    width: number;
    height: number;
    intensity?: number;
  }>;
}

export interface DetectionsFile {
  filename: string;
  events: DetectionEvent[];
}
