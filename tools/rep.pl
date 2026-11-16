#!/usr/bin/perl
# Точна заміна фрагментів у файлах.
#   perl rep.pl [file] < instructions
# Інструкції: рядок "@@@ шлях" перемикає файл; далі блоки "old\n=====>\nnew", розділені рядком "#####".
# Кожен old має траплятися у файлі рівно один раз; інакше жоден файл не змінюється.
use strict; use warnings;
my $current = shift;
local $/; my $in = <STDIN>; $in =~ s/\r\n/\n/g;
my (%text, %crlf, @order);
sub load {
  my $f = shift; return if exists $text{$f};
  open(my $h, '<:raw', $f) or die "no file $f\n"; my $s = <$h>; close $h;
  $crlf{$f} = ($s =~ /\r\n/) ? 1 : 0; $s =~ s/\r\n/\n/g; $text{$f} = $s; push @order, $f;
}
my @sections = split /^@@@ +(.+)\n/m, $in;
my $first = shift @sections;
unshift @sections, $current, $first if defined $current && $first =~ /\S/;
my $n = 0;
while (@sections) {
  my $file = shift @sections; my $body = shift @sections; $file =~ s/\s+$//;
  load($file);
  for my $blk (split /^#####\n/m, $body) {
    next unless $blk =~ /\S/;
    my ($old, $new) = split /^=====>\n/m, $blk, 2; die "bad block in $file\n" unless defined $new;
    chomp $old; chomp $new; $n++;
    my $c = () = $text{$file} =~ /\Q$old\E/g;
    die "block $n: found $c times in $file:\n$old\n" unless $c == 1;
    my $pos = index($text{$file}, $old);
    substr($text{$file}, $pos, length($old)) = $new;
  }
}
for my $f (@order) {
  my $s = $text{$f}; $s =~ s/\n/\r\n/g if $crlf{$f};
  open(my $h, '>:raw', $f) or die; print $h $s; close $h;
}
print "ok: $n blocks in " . scalar(@order) . " files\n";
