type Props = {
  segments: string[];
};

export function ReportLocationHeader({ segments }: Props) {
  if (segments.length === 0) {
    return null;
  }

  return (
    <div
      className="bg-neutral-100 border border-neutral-200 rounded-lg px-3 py-2"
      aria-label={segments.join(' › ')}
    >
      <p className="text-xs text-neutral-600 leading-relaxed">
        {segments.join(' › ')}
      </p>
    </div>
  );
}
