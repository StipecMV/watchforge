import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChannelGroup, ConversionStatus, DetectionsFile } from '../models/video.model';

const BASE = 'http://localhost:5000';

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private http: HttpClient) {}

  getVideos(): Observable<ChannelGroup[]> {
    return this.http.get<ChannelGroup[]>(`${BASE}/api/videos`);
  }

  getConversionStatus(id: string): Observable<ConversionStatus> {
    return this.http.get<ConversionStatus>(`${BASE}/api/videos/${id}/conversion-status`);
  }

  getDetections(id: string): Observable<DetectionsFile> {
    return this.http.get<DetectionsFile>(`${BASE}/api/videos/${id}/detections`);
  }

  streamUrl(id: string): string {
    return `${BASE}/api/videos/${id}/stream`;
  }
}
