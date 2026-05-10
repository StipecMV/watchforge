import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChannelGroup, VideoItem } from '../../models/video.model';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  @Input() channels: ChannelGroup[] = [];
  @Input() selectedVideoId: string | null = null;
  @Output() videoSelected = new EventEmitter<VideoItem>();

  collapsed = false;
  expandedChannels = new Set<string>();

  toggleSidebar() {
    this.collapsed = !this.collapsed;
  }

  toggleChannel(channelId: string) {
    if (this.expandedChannels.has(channelId)) {
      this.expandedChannels.delete(channelId);
    } else {
      this.expandedChannels.add(channelId);
    }
  }

  isExpanded(channelId: string): boolean {
    return this.expandedChannels.has(channelId);
  }

  selectVideo(video: VideoItem) {
    this.videoSelected.emit(video);
  }

  formatTime(video: VideoItem): string {
    return `${video.date} ${video.startTime}–${video.endTime}`;
  }
}
