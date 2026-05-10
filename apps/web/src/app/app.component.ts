import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from './services/api.service';
import { ChannelGroup, VideoItem } from './models/video.model';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { VideoPlayerComponent } from './components/video-player/video-player.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, SidebarComponent, VideoPlayerComponent],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  channels: ChannelGroup[] = [];
  selectedVideo: VideoItem | null = null;
  loadError = false;

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadVideos();
  }

  loadVideos() {
    this.loadError = false;
    this.api.getVideos().subscribe({
      next: data => {
        this.channels = data;
      },
      error: () => {
        this.loadError = true;
      }
    });
  }

  onVideoSelected(video: VideoItem) {
    this.selectedVideo = video;
  }
}
